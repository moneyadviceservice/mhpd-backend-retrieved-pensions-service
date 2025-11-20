using MhpdCommon.Models.Configuration;
using MhpdCommon.Models.MHPDModels;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;

namespace RetrievedPensionsRecordFunction.Repository;

public class PensionRecordRepository(CosmosClient cosmosClient, IOptions<CosmosBusinessConfiguration> config, ILogger<PensionRecordRepository> logger) 
    : IPensionRecordRepository
{
    private readonly Container _container = cosmosClient.GetContainer(config.Value.DatabaseId, config.Value.RetrievedPensionsContainer);

    public async Task<List<RetrievedPensionRecord>> GetRetrievedRecordsAsync(string userSessionId, string? category = null, string? assetId = null)
    {
        var response = await GetRecordsAsync(userSessionId, category, assetId);
        List<RetrievedPensionRecord> records = [.. response];

        if (assetId is not null)
        {
            //if we are querying by asset id, we need to return all records with the same pension link id
            var detailRecord = records.SingleOrDefault();

            if (detailRecord?.PensionLinkId is not null)
            {
                var allRecords = await GetRecordsAsync(userSessionId);
                records.AddRange(allRecords.Where(record => record.PensionLinkId == detailRecord.PensionLinkId && record.AssetId != detailRecord.AssetId));
            }
        }

        return records;
    }

    public async Task<List<string>> GetRetrievedPeisAsync(string userSessionId)
    {
        var response = await GetRecordsAsync(userSessionId);

        return [.. response.Select(record => record.Pei!)];
    }

    public async Task<bool> SaveRetrievedPensionRecordAsync(string? correlationId, RetrievedPensionRecord record)
    {
        LogDatabaseInfo();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            logger.LogError("Correlation Id is null.");
            return false;
        }

        var response = await _container.UpsertItemAsync(record, new PartitionKey(record.UserSessionId), null, default);

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

    public async Task<int> DeleteRetrievedRecordsAsync(string userSessionId)
    {
        var response = await GetRecordsAsync(userSessionId);

        foreach (var record in response)
        {
            await _container.DeleteItemAsync<RetrievedPensionRecord>(record.Id, new PartitionKey(record.UserSessionId));
        }

        return response.Count;
    }

    private Task<FeedResponse<RetrievedPensionRecord>> GetRecordsAsync(string userSessionId, string? category = null, string? assetId = null)
    {
        var queryBuilder = new StringBuilder("SELECT * FROM c WHERE 1=1");
        var parameters = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(userSessionId))
        {
            queryBuilder.Append(" AND c.userSessionId = @sessionId");
            parameters["@sessionId"] = userSessionId;
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

        var iterator = _container.GetItemQueryIterator<RetrievedPensionRecord>(queryDefinition);

        return iterator.ReadNextAsync();
    }

    private void LogDatabaseInfo()
    {
        var connDetails = $"Accessing Cosmos DB container: [{_container.Id}] in the database [{_container.Database}]";

        logger.LogInformation(connDetails);
    }
}
