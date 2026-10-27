using Azure.Messaging.ServiceBus;
using MhpdCommon.Constants;
using MhpdCommon.ErrorHandling;
using MhpdCommon.Extensions;
using MhpdCommon.Models.MessageBodyModels;
using MhpdCommon.Models.MHPDModels;
using MhpdCommon.Utils;
using MhpdCommon.ViewData;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RetrievedPensionsRecordFunction.Models;
using RetrievedPensionsRecordFunction.Repository;
using System.Text;
using System.Text.Json;
using static MhpdCommon.ViewData.EvaluationConstants;

namespace RetrievedPensionsRecordFunction;

public class RetrievedPensionsFunction(ILogger<RetrievedPensionsFunction> logger,
    IIdValidator idValidator,
    IMessageParser messageParser,
    IPensionRecordRepository pensionRepository,
    IArrangementProcessor arrangementProcessor)
{
    private const string InvalidPayloadResponse = "Invalid retrieved pension payload";

    [Function(nameof(RetrievedPensionsFunction))]
    public async Task Run(
        [ServiceBusTrigger("%CommonServiceBusConfiguration:InboundQueue%", Connection = "ServiceBusConnectionstring", IsBatched = true)]
        ServiceBusReceivedMessage[] messages,
        ServiceBusMessageActions messageActions)
    {
        var messageTasks = messages.Select(m => HandleMessage(m, messageActions)).ToList();
        await Task.WhenAll(messageTasks);
    }

    private async Task HandleMessage(ServiceBusReceivedMessage message, ServiceBusMessageActions messageActions)
    {
        if (!idValidator.IsValidGuid(message.CorrelationId))
        {
            var Idresponse = $"Missing or Invalid correlationId: {message.CorrelationId}";
            logger.LogCritical(Idresponse);
            await messageActions.DeadLetterMessageAsync(message, deadLetterReason: Idresponse);
            return;
        }

        using var scope = logger.BeginCorrelationScope(message.CorrelationId, Constants.QueueLogSource);

        var enqueudTimespan = DateTimeOffset.UtcNow - message.EnqueuedTime;
        if (enqueudTimespan.TotalSeconds > 5)
        {
            logger.LogMhpdWarning("Message with MessageId: {MessageId}, CorrelationId: {CorrelationId} has been in the queue for {EnqueudTimespan}.", message.MessageId, message.CorrelationId, enqueudTimespan);
        }

        try
        {
            var payload = await ExtractAndValidateMessagePayloadAsync(message);
            await pensionRepository.SaveRetrievedPensionRecordAsync(payload);
            await messageActions.CompleteMessageAsync(message);

            logger.LogMhpdWarning(
            "Retrieved Pension Record with AssetId: {AssetId}, from SchemeName: {SchemeName}, Administrator: {Administrator}",
            payload.AssetId,
            payload.SchemeName,
            payload.Administrator);
        }
        catch (Exception error)
        {
            logger.LogCritical(error, error.Message);
            await messageActions.DeadLetterMessageAsync(message, deadLetterReason: error.Message);
        }
    }

    private async Task<RetrievedPensionRecord> ExtractAndValidateMessagePayloadAsync(ServiceBusReceivedMessage message)
    {
        var messageBody = Encoding.UTF8.GetString(message.Body);
        string? logMessage;
        RetrievedPensionDetailsPayload? payload;
        var (pei, userSessionId, pensionLinkId) = GetPayloadIds(messageBody);

        try
        {
            var classifiedPayload = await arrangementProcessor.ProcessArrangementAsync(messageBody);
            payload = await messageParser.ToRetrievedPensionPayloadAsync(classifiedPayload);

            ArgumentNullException.ThrowIfNull(payload);

            var root = JsonDocument.Parse(classifiedPayload).RootElement;
            var resultNode = root.GetProperty(PensionConstants.RetrievalResult);

            var record = new RetrievedPensionRecord
            {
                CorrelationId = message.CorrelationId,
                Pei = pei,
                UserSessionId = userSessionId,
                PensionLinkId = pensionLinkId,
                AssetId = GetAssetId(resultNode, pei),
                Category = GetCategory(resultNode),
                SchemeName = GetSchemeName(resultNode),
                PensionType = GetPensionType(resultNode),
                MatchType = GetMatchType(resultNode),
                HasIncome = GetIncome(resultNode),
                IsMcCloudPension = GetMcCloud(resultNode),
                Administrator = GetAdministrator(resultNode),
                RetrievalResult = payload.RetrievalResult,
            };

            return record;
        }
        catch (AggregateException error)
        {
            var builder = new StringBuilder(InvalidPayloadResponse);
            builder.AppendLine();
            foreach (var ex in error.InnerExceptions)
            {
                builder.AppendLine(ex.Message);
            }

            logMessage = builder.ToString();
            var schemeName = GetSchemeName(messageBody);
            var errorCode = PensionProviderConstants.RetrievalErrorCodes.DataSchemaInvalid;
            var errorContext = ErrorContext.FromErrorCode(errorCode, schemeName);
            logger.LogServiceError(logMessage, errorContext, error);

            return CreateFailedRetrievedPension(pei, userSessionId, message.CorrelationId, errorCode);
        }
        catch (Exception error)
        {
            logMessage = $"{InvalidPayloadResponse}: {error.Message}";
            var schemeName = GetSchemeName(messageBody);
            var errorCode = PensionProviderConstants.RetrievalErrorCodes.SystemError;
            var errorContext = ErrorContext.FromErrorCode(errorCode, schemeName);
            logger.LogServiceError(logMessage, errorContext, error);
            return CreateFailedRetrievedPension(pei, userSessionId, message.CorrelationId, errorCode);
        }
    }

    private static string GetCategory(JsonElement resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.PensionCategory, Category.Unsupported);
    }

    private string GetAssetId(JsonElement resultNode, string pei)
    {
        idValidator.TryExtractPei(pei, out _, out var assetId);
        return GetArrangementProperty(resultNode, PensionConstants.ExternalAssetId, assetId, assetId);
    }

    private static string GetSchemeName(JsonElement resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.SchemeName, Constants.UnkonwnPensionScheme, string.Empty);
    }

    private static string GetSchemeName(string? viewData)
    {
        if (string.IsNullOrEmpty(viewData))
        {
            return Constants.UnkonwnPensionScheme;
        }

        var root = JsonDocument.Parse(viewData).RootElement;
        var resultNode = root.GetProperty(PensionConstants.RetrievalResult);

        var schemeName = GetSchemeName(resultNode);

        return string.IsNullOrEmpty(schemeName) ? Constants.UnkonwnPensionScheme : schemeName;
    }

    private static string GetPensionType(JsonElement resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.PensionType, Constants.UnkonwnPensionType, Constants.UnkonwnPensionType);
    }

    private static string GetMatchType(JsonElement resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.MatchType, Constants.UnkonwnMatchType, Constants.UnkonwnMatchType);
    }

    private static bool GetIncome(JsonElement resultNode)
    {
        return GetBooleanProperty(resultNode, PensionConstants.HasIncome);
    }

    private static bool GetMcCloud(JsonElement resultNode)
    {
        return GetBooleanProperty(resultNode, PensionConstants.HasMultipleIncomeOptions);
    }

    private static bool GetBooleanProperty(JsonElement resultNode, string propertyPath)
    {
        var pathValue = GetArrangementProperty(resultNode, propertyPath, Boolean.FalseString, Boolean.FalseString);
        return bool.TryParse(pathValue, out var isSet) && isSet;
    }

    private static string GetAdministrator(JsonElement resultNode)
    {
        return GetArrangementProperty(resultNode, $"{PensionConstants.PensionAdministrator}.name", Constants.UnkonwnAdministrator, Constants.UnkonwnAdministrator);
    }

    private static string GetArrangementProperty(JsonElement resultNode, string propertyPath, string defaultValue, string valueOnError = Category.Error)
    {
        if (resultNode.ValueKind == JsonValueKind.Object && resultNode.TryGetProperty(PensionConstants.ErrorCode, out _))
        {
            return valueOnError;
        }

        var segments = propertyPath.Split('.');
        JsonElement current = resultNode;

        foreach (var segment in segments)
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var next))
            {
                current = next;
            }
            else
            {
                return defaultValue;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? defaultValue,
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => Boolean.TrueString,
            JsonValueKind.False => Boolean.FalseString,
            _ => current.ValueKind == JsonValueKind.Null ? defaultValue : current.GetRawText()?.Trim('"') ?? defaultValue
        };
    }

    private (string pei, string userSessionId, string? pensionLinkId) GetPayloadIds(string? messagePayload)
    {
        try
        {
            using var doc = JsonDocument.Parse(messagePayload!);
            var root = doc.RootElement;

            var pei = GetPayloadProperty(root, PensionConstants.Pei);
            var userSessionId = GetPayloadProperty(root, PensionConstants.UserSessionId);
            var pensionLinkId = GetPayloadProperty(root, PensionConstants.ExternalPensionPolicyId);

            return (pei!, userSessionId!, pensionLinkId);
        }
        catch (Exception error)
        {
            var context = ErrorContext.FromErrorCode(PensionProviderConstants.RetrievalErrorCodes.DataPayloadInvalid);
            logger.LogServiceError("Unable to read retrieved pension details.", context, error);
            throw;
        }
    }

    private static string? GetPayloadProperty(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static RetrievedPensionRecord CreateFailedRetrievedPension(string pei, string userSessionId, string correlationId, string errorCode)
    {
        var error = @"{""errorCode"": """ + errorCode + @"""}";

        return new RetrievedPensionRecord
        {
            Pei = pei,
            CorrelationId = correlationId,
            UserSessionId = userSessionId,
            PensionType = Constants.UnkonwnPensionType,
            MatchType = Constants.UnkonwnMatchType,
            AssetId = Guid.NewGuid().ToString(),
            Category = Category.Error,
            SchemeName = Constants.UnkonwnPensionScheme,
            HasIncome = false,
            IsMcCloudPension = false,
            Administrator = Constants.UnkonwnAdministrator,
            RetrievalResult = JsonSerializer.Deserialize<dynamic>(error)
        };
    }
}
