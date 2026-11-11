using MhpdCommon.Models.MHPDModels;

namespace RetrievedPensionsRecordFunction.Repository;

public interface IPensionRecordRepository
{
    Task SaveRetrievedPensionRecordAsync(params RetrievedPensionRecord[] records);

    Task<List<RetrievedPensionRecord>> GetRetrievedRecordsAsync(string userSessionId, string? category = null, string? assetId = null);

    Task<List<string>> GetRetrievedPeisAsync(string userSessionId);

    Task DeleteRetrievedRecordsAsync(string userSessionId);
}
