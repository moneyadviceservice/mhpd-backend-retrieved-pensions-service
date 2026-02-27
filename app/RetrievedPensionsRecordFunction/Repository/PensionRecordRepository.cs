using MhpdCommon.Models.MHPDModels;
using MhpdCommon.Repository;
using Microsoft.Extensions.Logging;

namespace RetrievedPensionsRecordFunction.Repository;

public class PensionRecordRepository(ILogger<PensionRecordRepository> logger, IRetrievedPensionRecordRedisRepository retrievedPensionRecordRepository) 
    : IPensionRecordRepository
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
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            logger.LogError("Correlation Id is null.");
            return false;
        }

        await retrievedPensionRecordRepository.UpsertItemAsync(record);
        return true;
    }

    public async Task DeleteRetrievedRecordsAsync(string userSessionId)
    {
        var response = await retrievedPensionRecordRepository.DeleteByIdUserSessionIdAsync(userSessionId);
    }

    private async Task<List<RetrievedPensionRecord>> GetRecordsAsync(string userSessionId, string? category = null, string? assetId = null)
    {
        var records = await retrievedPensionRecordRepository.GetAllByUserSessionIdAsync(userSessionId);
        return records.Where(r =>
            (category == null || r.Category == category) &&
            (assetId == null || r.AssetId == assetId)).ToList();
    }
}
