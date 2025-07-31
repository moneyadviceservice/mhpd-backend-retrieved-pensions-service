using MhpdCommon.Constants;
using MhpdCommon.Constants.HttpClient;
using MhpdCommon.Extensions;
using MhpdCommon.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using RetrievedPensionsRecordFunction.Models;
using RetrievedPensionsRecordFunction.Repository;
using System.Net;

namespace RetrievedPensionsRecordFunction
{
    public class RetrievedRecordsFunction(ILogger<RetrievedRecordsFunction> logger, IPensionRecordRepository repository, IIdValidator validator)
    {
        [Function("GetRetrievedRecords")]
        [OpenApiOperation(operationId: "get-retrieved-pensions-records",
            Summary = "Get Retrieved Pension Records",
            Description = "Get the retrieved retrieved-pensions-records that contains pensions information has been retrieved from the PDP Ecosystem for peis.")]
        [OpenApiParameter(
            QueryParams.RetrievedPensions.RetrievalRecordId,
            In = ParameterLocation.Query,
            Description = "The id of the pensions retrieval record that the retrieved pension record is associated with.",
            Required = true)]
        [OpenApiParameter(
            QueryParams.RetrievedPensions.PensionCategory,
            In = ParameterLocation.Query,
            Description = "Gets a subset of the pensions retrieval records that have the specified classification.",
            Required = false)]
        [OpenApiParameter(
            QueryParams.RetrievedPensions.AssetId,
            In = ParameterLocation.Query,
            Description = "Gets a specific pensions retrieval record with the specified Id.",
            Required = false)]
        [OpenApiParameter(
        HeaderConstants.CorrelationId,
        In = ParameterLocation.Header,
        Description = "An Id with which to group all logging statements made during a single session",
        Required = false)]
        [OpenApiResponseWithBody(HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(string), 
            Description = "The array of Retrieved Pension Records that match the provided query parameters")]
        [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "BadRequest")]
        public async Task<IActionResult> GetAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "retrieved-pension-records")] HttpRequest req)
        {
            var category = req.Query[QueryParams.RetrievedPensions.PensionCategory];
            var assetId = req.Query[QueryParams.RetrievedPensions.AssetId];

            return await ProcessRetrievedRecordsAsync(req, Constants.HttpGetLogSource, id => repository.GetRetrievedRecordsAsync(id, category, assetId));
        }

        [Function("DeleteRetrievedRecords")]
        [OpenApiOperation(operationId: "delete-pensions-retrieved-records-id",
            Summary = "Delete Pensions Retrieved Record",
            Description = "Deletes the given pension retrieved record id.")]
        [OpenApiParameter(
            QueryParams.RetrievedPensions.RetrievalRecordId,
            In = ParameterLocation.Query,
            Description = "The id of the pensions retrieval record that the retrieved pension record is associated with.",
            Required = true)]
        [OpenApiParameter(
        HeaderConstants.CorrelationId,
        In = ParameterLocation.Header,
        Description = "An Id with which to group all logging statements made during a single session",
        Required = false)]
        [OpenApiResponseWithBody(HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(int),
            Description = "The number of records deleted as part of the request")]
        public async Task<IActionResult> DeleteAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "retrieved-pension-records")] HttpRequest req)
        {
            return await ProcessRetrievedRecordsAsync(req, Constants.HttpDeleteLogSource, repository.DeleteRetrievedRecordsAsync);
        }

        private async Task<IActionResult> ProcessRetrievedRecordsAsync<T>(HttpRequest req, string logSource, Func<string, Task<T>> processor)
        {
            var correlationId = req.Headers[HeaderConstants.CorrelationId].ToString();

            if (string.IsNullOrEmpty(correlationId))
            {
                correlationId = Guid.NewGuid().ToString();
            }

            if (!validator.IsValidGuid(correlationId))
            {
                return new BadRequestObjectResult(Constants.InvalidCorrelationId);
            }

            using var scope = logger.BeginCorrelationScope(correlationId, logSource);

            var pensionsRetrievalRecordId = req.Query[QueryParams.RetrievedPensions.RetrievalRecordId].ToString();

            logger.LogRequest($"Pension retrieval record Id: {pensionsRetrievalRecordId}");

            if (!validator.IsValidGuid(pensionsRetrievalRecordId))
            {
                logger.LogError("Unable to service request for pensionsRetrievalRecordId [{RetrievalId}]: {Reason}", pensionsRetrievalRecordId, Constants.InvalidRecordId);
                return new BadRequestObjectResult(Constants.InvalidRecordId);
            }

            var records = await processor(pensionsRetrievalRecordId);

            logger.LogResponse(records);

            return new OkObjectResult(records);
        }
    }
}
