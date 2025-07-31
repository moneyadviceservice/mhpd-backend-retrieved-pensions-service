using MhpdCommon.Models.MHPDModels;

namespace RetrievedPensionsRecordFunction.Repository;

public interface IPensionRecordRepository
{
    Task<bool> SaveRetrievedPensionRecordAsync(string? correlationId, RetrievedPensionRecord record);

    Task<List<RetrievedPensionRecord>> GetRetrievedRecordsAsync(string pensionsRetrievalRecordId, string? category = null, string? assetId = null);

    Task<int> DeleteRetrievedRecordsAsync(string pensionsRetrievalRecordId);
}
