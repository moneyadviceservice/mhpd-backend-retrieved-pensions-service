namespace RetrievedPensionsRecordFunction.Models;

public static class Constants
{
    public const string QueueLogSource = "Retrieved Pensions Message Queue";
    public const string HttpGetLogSource = "Retrieved Pensions Http GET";
    public const string HttpDeleteLogSource = "Retrieved Pensions Http DELETE";
    public const string InvalidRecordId = "pensionsRetrievalRecordId missing or invalid";
    public const string InvalidCorrelationId = "mhpdCorrelationId invalid";
    public const string InvalidQueryFilters = "Filtering by category requires a retrieval Id.";
    public const string MissingQueryFilters = "At least one of sessionId or externalAssetId must be provided.";
    public const string UnkonwnPensionScheme = "Unknown Pension Scheme";
    public const string UnkonwnPensionType = "Unknown Pension Type";
    public const string UnkonwnMatchType = "Unknown Match Type";
}
