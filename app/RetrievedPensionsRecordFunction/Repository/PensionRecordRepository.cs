using MhpdCommon.Models.Configuration;
using MhpdCommon.Models.MHPDModels;
using MhpdCommon.Repository;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace RetrievedPensionsRecordFunction.Repository;

public class PensionRecordRepository(CosmosClient cosmosClient, IOptions<CosmosBusinessConfiguration> configuration, ILogger<PensionRecordRepository> logger) 
    : CosmosDbRepository<RetrievedPensionRecord>(cosmosClient, configuration.Value.DatabaseId, configuration.Value.RetrievedPensionsContainer), IPensionRecordRepository
{
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

        var existingPensions = await GetRetrievedRecordsAsync(record.UserSessionId, assetId: record.AssetId);

        if (existingPensions.Count != 0)
        {
            var existingPension = existingPensions.Single(pension => pension.AssetId == record.AssetId);

            record.Id = existingPension.Id; // preserve the same id to update the existing record instead of creating a new one
            logger.LogWarning("Updating retrieved pension record for PEI: {Pei}", record.Pei);
        }

        var response = await Container.UpsertItemAsync(record, new PartitionKey(record.UserSessionId), null, default);

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

    public async Task DeleteRetrievedRecordsAsync(string userSessionId)
    {
        var response = await Container.DeleteAllItemsByPartitionKeyStreamAsync(new PartitionKey(userSessionId));
        if (!response.IsSuccessStatusCode)
        {
            throw new CosmosException(
                response.ErrorMessage,
                response.StatusCode,
                0,
                response.Headers.ActivityId,
                response.Headers.RequestCharge);
        }
    }

    private Task<FeedResponse<RetrievedPensionRecord>> GetRecordsAsync(string userSessionId, string? category = null, string? assetId = null)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(category))
        {
            conditions.Add("c.category = @category");
            parameters["@category"] = category;
        }

        if (!string.IsNullOrWhiteSpace(assetId))
        {
            conditions.Add("c.assetId = @assetId");
            parameters["@assetId"] = assetId;
        }

        var queryDefinition = conditions.Count > 0
            ? new QueryDefinition($"SELECT * FROM c WHERE {string.Join(" AND ", conditions)}")
            : new QueryDefinition($"SELECT * FROM c");

        foreach (var param in parameters)
        {
            queryDefinition.WithParameter(param.Key, param.Value);
        }

        var iterator = Container.GetItemQueryIterator<RetrievedPensionRecord>(queryDefinition, null, new QueryRequestOptions { PartitionKey = new PartitionKey(userSessionId) });

        return iterator.ReadNextAsync();
    }

    private void LogDatabaseInfo()
    {
        var connDetails = $"Accessing Cosmos DB container: [{Container.Id}] in the database [{Container.Database}]";

        logger.LogInformation(connDetails);
    }
}
