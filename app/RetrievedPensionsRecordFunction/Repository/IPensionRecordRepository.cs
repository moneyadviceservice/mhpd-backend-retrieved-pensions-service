using MhpdCommon.Models.MHPDModels;

namespace RetrievedPensionsRecordFunction.Repository;

public interface IPensionRecordRepository
{
    Task<bool> SaveRetrievedPensionRecordAsync(string? correlationId, RetrievedPensionRecord record);

    Task<List<RetrievedPensionRecord>> GetRetrievedRecordsAsync(string userSessionId, string? category = null, string? assetId = null);

    Task<List<string>> GetRetrievedPeisAsync(string userSessionId);

    Task<int> DeleteRetrievedRecordsAsync(string userSessionId);
}
