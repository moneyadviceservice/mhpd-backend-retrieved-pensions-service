using MhpdCommon.Models.Configuration;
using MhpdCommon.Models.MHPDModels;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace RetrievedPensionsRecordFunction.Repository;

public class PensionRecordRepository(CosmosClient cosmosClient, IOptions<CosmosBusinessConfiguration> config, ILogger<PensionRecordRepository> logger) 
    : IPensionRecordRepository
{
    private readonly CosmosBusinessConfiguration _configuration = config.Value;

    public async Task<List<RetrievedPensionRecord>> GetRetrievedRecordsAsync(string pensionsRetrievalRecordId)
    {
        var container = cosmosClient.GetContainer(_configuration.DatabaseId, _configuration.RetrievedPensionsContainer);
        using var iterator = GetRetrievedRecords(container, pensionsRetrievalRecordId);

        var response = await iterator.ReadNextAsync();

        return [.. response];
    }

    public async Task<bool> SaveRetrievedPensionRecordAsync(string? correlationId, RetrievedPensionRecord record)
    {
        LogDatabaseInfo();

        if (record is null)
        {
            logger.LogCritical("Retrieved pension record is null.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            logger.LogError("Correlation Id is null.");
            return false;
        }

        //var record = new RetrievedPensionRecord
        //{
        //    Id = Guid.NewGuid().ToString(),
        //    CorrelationId = correlationId,
        //    Pei = payload.Pei,
        //    PensionsRetrievalRecordId = payload.PensionRetrievalRecordId,
        //    RetrievalResult = payload.RetrievalResult
        //};

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

    private static FeedIterator<RetrievedPensionRecord> GetRetrievedRecords(Container container, string pensionsRetrievalRecordId)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.pensionsRetrievalRecordId = @retrievalId")
                .WithParameter("@retrievalId", pensionsRetrievalRecordId);

        return container.GetItemQueryIterator<RetrievedPensionRecord>(query);
    }

    private void LogDatabaseInfo()
    {
        var connDetails = $"Accessing Cosmos DB container: [{_configuration.RetrievedPensionsContainer}] in the database [{_configuration.DatabaseId}]";

        logger.LogInformation(connDetails);
    }
}
