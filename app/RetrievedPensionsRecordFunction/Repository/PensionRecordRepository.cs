using MhpdCommon.Models.Configuration;
using MhpdCommon.Models.MHPDModels;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using static MhpdCommon.ViewData.PensionEnums;
using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;

namespace RetrievedPensionsRecordFunction.Repository;

public class PensionRecordRepository(CosmosClient cosmosClient, IOptions<CosmosBusinessConfiguration> config, ILogger<PensionRecordRepository> logger) 
    : IPensionRecordRepository
{
    private readonly CosmosBusinessConfiguration _configuration = config.Value;

    public async Task<List<RetrievedPensionRecord>> GetRetrievedRecordsAsync(string pensionsRetrievalRecordId, string? category = null, string? assetId = null)
    {
        var container = cosmosClient.GetContainer(_configuration.DatabaseId, _configuration.RetrievedPensionsContainer);
        using var iterator = GetRetrievedRecords(container, pensionsRetrievalRecordId, category, assetId);

        var response = await iterator.ReadNextAsync();

        return [.. response];
    }

    public async Task<bool> SaveRetrievedPensionRecordAsync(string? correlationId, RetrievedPensionRecord record)
    {
        LogDatabaseInfo();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            logger.LogError("Correlation Id is null.");
            return false;
        }

        Container container = cosmosClient.GetContainer(_configuration.DatabaseId, _configuration.RetrievedPensionsContainer);

        var response = await container.UpsertItemAsync(record, new PartitionKey(record.PensionsRetrievalRecordId), null, default);

        string? logMessage;

        if (response.StatusCode == HttpStatusCode.OK ||
            response.StatusCode == HttpStatusCode.Created)
        {
            logMessage = $"Retrieved pension record for PEI: {record.Pei} " +
                $"{(response.StatusCode == HttpStatusCode.Created ? "created" : "updated")}.";

            logger.LogWarning(logMessage);
            return true;
        }

        logMessage = $"Unable to save a record for pension with PEI: {record.Pei}";
        logger.LogCritical(logMessage);
        return false;
    }

    public async Task<int> DeleteRetrievedRecordsAsync(string pensionsRetrievalRecordId)
    {
        var container = cosmosClient.GetContainer(_configuration.DatabaseId, _configuration.RetrievedPensionsContainer);
        using var iterator = GetRetrievedRecords(container, pensionsRetrievalRecordId);

        var response = await iterator.ReadNextAsync();

        foreach (var record in response)
        {
            await container.DeleteItemAsync<RetrievedPensionRecord>(record.Id, new PartitionKey(record.PensionsRetrievalRecordId));
        }

        return response.Count;
    }

    private static FeedIterator<RetrievedPensionRecord> GetRetrievedRecords(Container container, string pensionsRetrievalRecordId, string? category = null, string? assetId = null)
    {
        var queryBuilder = new StringBuilder("SELECT * FROM c WHERE 1=1");
        var parameters = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(pensionsRetrievalRecordId))
        {
            queryBuilder.Append(" AND c.pensionsRetrievalRecordId = @retrievalId");
            parameters["@retrievalId"] = pensionsRetrievalRecordId;
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            queryBuilder.Append(" AND c.category = @category");
            parameters["@category"] = category;
        }

        if (!string.IsNullOrWhiteSpace(assetId))
        {
            queryBuilder.Append(" AND c.assetId = @assetId");
            parameters["@assetId"] = assetId;
        }

        var queryDefinition = new QueryDefinition(queryBuilder.ToString());

        foreach (var param in parameters)
        {
            queryDefinition.WithParameter(param.Key, param.Value);
        }

        return container.GetItemQueryIterator<RetrievedPensionRecord>(queryDefinition);
    }

    private void LogDatabaseInfo()
    {
        var connDetails = $"Accessing Cosmos DB container: [{_configuration.RetrievedPensionsContainer}] in the database [{_configuration.DatabaseId}]";

        logger.LogInformation(connDetails);
    }
}
