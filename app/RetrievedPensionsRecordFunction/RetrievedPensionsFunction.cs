using Azure.Messaging.ServiceBus;
using MhpdCommon.Constants;
using MhpdCommon.Extensions;
using MhpdCommon.Models.MessageBodyModels;
using MhpdCommon.Models.MHPDModels;
using MhpdCommon.Utils;
using MhpdCommon.ViewData;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using RetrievedPensionsRecordFunction.Models;
using RetrievedPensionsRecordFunction.Repository;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        [ServiceBusTrigger("%CommonServiceBusConfiguration:InboundQueue%", Connection = "ServiceBusConnectionstring")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
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
            var payload = ExtractAndValidateMessagePayload(message);

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

    private RetrievedPensionRecord ExtractAndValidateMessagePayload(ServiceBusReceivedMessage message)
    {
        var messageBody = Encoding.UTF8.GetString(message.Body);
        string? logMessage;
        RetrievedPensionDetailsPayload? payload;

        var messagePayload = arrangementProcessor.ProcessArrangement(messageBody);

        try
        {
            payload = messageParser.ToRetrievedPensionPayload(messagePayload);
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
            throw new InvalidDataException(logMessage, error);
        }

        ArgumentNullException.ThrowIfNull(payload);

        var root = JsonNode.Parse(messagePayload)?.AsObject();
        var resultNode = root?[PensionConstants.RetrievalResult];

        var record = new RetrievedPensionRecord
        {
            Id = Guid.NewGuid().ToString(),
            CorrelationId = message.CorrelationId,
            Pei = payload.Pei,
            AssetId = GetAssetId(resultNode),
            Category = GetCategory(resultNode),
            SchemeName = GetSchemeName(resultNode),
            PensionType = GetPensionType(resultNode),
            MatchType = GetMatchType(resultNode),
            HasIncome = GetIncome(resultNode),
            Administrator = GetAdministrator(resultNode),
            PensionsRetrievalRecordId = payload.PensionRetrievalRecordId,
            RetrievalResult = payload.RetrievalResult
        };

        return record;
    }

    private static string GetCategory(JsonNode? resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.PensionCategory, EvaluationConstants.Category.Unsupported);
    }

    private static string GetAssetId(JsonNode? resultNode)
    {
        var defaultValue = Guid.NewGuid().ToString();
        return GetArrangementProperty(resultNode, PensionConstants.ExternalAssetId, defaultValue, defaultValue);
    }

    private static string GetSchemeName(JsonNode? resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.SchemeName, Constants.UnkonwnPensionScheme);
    }

    private static string GetPensionType(JsonNode? resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.PensionType, Constants.UnkonwnPensionType);
    }

    private static string GetMatchType(JsonNode? resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.MatchType, Constants.UnkonwnMatchType);
    }

    private static string GetIncome(JsonNode? resultNode)
    {
        return GetArrangementProperty(resultNode, PensionConstants.HasIncome, "false");
    }

    private static string GetAdministrator(JsonNode? resultNode)
    {
        return GetArrangementProperty(resultNode, $"{PensionConstants.PensionAdministrator}.name", Constants.UnkonwnAdministrator);
    }

    private static string GetArrangementProperty(JsonNode? resultNode, string propertyPath, string defaultValue, string valueOnError = EvaluationConstants.Category.Error)
    {
        if(resultNode == null || resultNode.GetValueKind() != JsonValueKind.Array)
        {
            return valueOnError;
        }

        var resultArray = resultNode.AsArray();
        if (resultArray.FirstOrDefault() is not JsonObject firstObj) return defaultValue;

        var segments = propertyPath.Split('.');
        JsonNode? currentNode = firstObj;

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

        return currentNode?.ToJsonString().Trim('"') ?? defaultValue;
    }

    private void LogRequestMesage(ServiceBusReceivedMessage receivedMessage)
    {
        var logMessage = $"Message Received - CorrelationId:[{receivedMessage.CorrelationId}], " +
            $"MessageId: [{receivedMessage.MessageId}], ContentType: [{receivedMessage.ContentType}] {Environment.NewLine}";
        logger.LogWarning("Message Details : {Details} Body: {Body}", logMessage, receivedMessage.Body);
    }
}

[ExcludeFromCodeCoverage]
public static class RetrievedPensionsFunctionOpenApiSpec
{
    private const string Tag = "items";

    [Function("GetItem")]
    [OpenApiOperation(operationId: "GetItem", tags: Tag)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(string))]
    public static HttpResponseData Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteString("Hello, OpenAPI!");
        return response;
    }
}
