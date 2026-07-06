using Azure.Messaging.ServiceBus;
using MhpdCommon.Constants;
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
using System.Text.Json.Nodes;
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

        LogRequestMesage(message);

        try
        {
            var payload = await ExtractAndValidateMessagePayloadAsync(message);

            if (await pensionRepository.SaveRetrievedPensionRecordAsync(message.CorrelationId, payload))
            {
                await messageActions.CompleteMessageAsync(message);
            }
            else
            {
                await messageActions.AbandonMessageAsync(message);
            }
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

            var root = JsonNode.Parse(classifiedPayload)?.AsObject();
            var resultNode = root?[PensionConstants.RetrievalResult];

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
            logger.LogCritical(error, logMessage);

            return CreateFailedRetrievedPension(pei, userSessionId, message.CorrelationId);
        }
        catch (Exception error)
        {
            logMessage = $"{InvalidPayloadResponse}: {error.Message}";
            logger.LogCritical(error, logMessage);
            return CreateFailedRetrievedPension(pei, userSessionId, message.CorrelationId);
        }
    }

    private static string GetCategory(JsonNode? resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.PensionCategory, Category.Unsupported);
    }

    private string GetAssetId(JsonNode? resultNode, string pei)
    {
        idValidator.TryExtractPei(pei, out _, out var assetId);
        return GetArrangementProperty(resultNode, PensionConstants.ExternalAssetId, assetId, assetId);
    }

    private static string GetSchemeName(JsonNode? resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.SchemeName, Constants.UnkonwnPensionScheme, string.Empty);
    }

    private static string GetPensionType(JsonNode? resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.PensionType, Constants.UnkonwnPensionType, Constants.UnkonwnPensionType);
    }

    private static string GetMatchType(JsonNode? resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.MatchType, Constants.UnkonwnMatchType, Constants.UnkonwnMatchType);
    }

    private static bool GetIncome(JsonNode? resultNode)
    {
        return GetBooleanProperty(resultNode, PensionConstants.HasIncome);
    }

    private static bool GetMcCloud(JsonNode? resultNode)
    {
        return GetBooleanProperty(resultNode, PensionConstants.HasMultipleIncomeOptions);
    }

    private static bool GetBooleanProperty(JsonNode? resultNode, string propertyPath)
    {
        var pathValue = GetArrangementProperty(resultNode, propertyPath, Boolean.FalseString, Boolean.FalseString);

        return bool.TryParse(pathValue, out var isSet) && isSet;
    }

    private static string GetAdministrator(JsonNode? resultNode)
    {
        return GetArrangementProperty(resultNode, $"{PensionConstants.PensionAdministrator}.name", Constants.UnkonwnAdministrator, Constants.UnkonwnAdministrator);
    }

    private static string GetArrangementProperty(JsonNode? resultNode, string propertyPath, string defaultValue, string valueOnError = Category.Error)
    {
        if(resultNode == null || (resultNode is JsonObject result && result.TryGetPropertyValue(PensionConstants.ErrorCode, out _)))
        {
            return valueOnError;
        }

        var segments = propertyPath.Split('.');
        JsonNode? currentNode = resultNode;

        foreach (var segment in segments)
        {
            if (currentNode is JsonObject obj && obj.TryGetPropertyValue(segment, out var next))
            {
                currentNode = next;
            }
            else
            {
                return defaultValue;
            }
        }

        return currentNode?.GetValue<object>()?.ToString()?.Trim('"') ?? defaultValue;
    }

    private static (string pei, string userSessionId, string? pensionLinkId) GetPayloadIds(string? messagePayload)
    {
        var resultNode = JsonNode.Parse(messagePayload!);
        var pei = GetPayloadProperty(resultNode, PensionConstants.Pei, $"{Guid.NewGuid()}:{Guid.NewGuid()}");
        var userSessionId = GetPayloadProperty(resultNode, PensionConstants.UserSessionId, $"{Guid.NewGuid()}");
        var pensionLinkId = GetPayloadProperty(resultNode, PensionConstants.ExternalPensionPolicyId, null);
        return (pei!, userSessionId!, pensionLinkId);
    }

    private static string? GetPayloadProperty(JsonNode? resultNode, string propertyName, string? defaultValue)
    {
        var property = resultNode?[propertyName]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(property) ? defaultValue : property;
    }

    private static RetrievedPensionRecord CreateFailedRetrievedPension(string pei, string userSessionId, string correlationId)
    {
        var error = @"{""errorCode"": """ + PensionProviderConstants.RetrievalErrorCodes.SystemError + @"""}";

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

    private void LogRequestMesage(ServiceBusReceivedMessage receivedMessage)
    {
        var enqueudTimespan = DateTimeOffset.UtcNow - receivedMessage.EnqueuedTime;
        if (enqueudTimespan.TotalSeconds > 5)
        {
            logger.LogWarning("Message with MessageId: {MessageId}, CorrelationId: {CorrelationId} has been in the queue for {EnqueudTimespan}.", receivedMessage.MessageId, receivedMessage.CorrelationId, enqueudTimespan);
        }

        var logMessage = $"Message Received - CorrelationId:[{receivedMessage.CorrelationId}], MessageId: [{receivedMessage.MessageId}], ContentType: [{receivedMessage.ContentType}] {Environment.NewLine}";
        logger.LogWarning("Message Details : {Details} Body: {Body}", logMessage, receivedMessage.Body);
    }
}
