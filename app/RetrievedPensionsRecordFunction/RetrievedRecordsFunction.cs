using MhpdCommon.Constants;
using MhpdCommon.Constants.HttpClient;
using MhpdCommon.Extensions;
using MhpdCommon.Models.MHPDModels;
using MhpdCommon.Models.OpenApi;
using MhpdCommon.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using RetrievedPensionsRecordFunction.Models;
using RetrievedPensionsRecordFunction.Repository;
using Swashbuckle.AspNetCore.Annotations;
using System.Net;

namespace RetrievedPensionsRecordFunction
{
    public class RetrievedRecordsFunction(ILogger<RetrievedRecordsFunction> logger, 
        IPensionRecordRepository repository, 
        IIdValidator validator,
        IServiceStatusProvider statusProvider)
    {
        [Function("GetRetrievedPeis")]
        [SwaggerOperation(
            OperationId = "get-retrieved-peis",
            Summary = "Get Retrieved Pension Records",
            Description = "Get the retrieved retrieved-pensions-records that contains pensions information has been retrieved from the PDP Ecosystem for peis.")]
        [PensionDataOpenApi]
        [SwaggerResponse((int)HttpStatusCode.OK, ContentTypes = ["application/json"], Type = typeof(string[]),
            Description = "The array of Retrieved Peis that match the provided query parameters")]
        [SwaggerResponse((int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> GetPeisAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "retrieved-peis")] HttpRequest req)
        {
            return await ProcessRetrievedRecordsAsync(req, Constants.PeisGetLogSource, id => repository.GetRetrievedPeisAsync(id));
        }

        [Function("GetRetrievedRecords")]
        [SwaggerOperation(
            OperationId = "get-retrieved-pensions-records",
            Summary = "Get Retrieved Pension Records",
            Description = "Get the retrieved retrieved-pensions-records that contains pensions information has been retrieved from the PDP Ecosystem for peis.")]
        [PensionDataOpenApi]
        [OpenApiParameter(
            QueryParams.RetrievedPensions.PensionCategory,
            ParameterLocation.Query,
            "Gets a subset of the pensions retrieval records that have the specified classification.",
            false)]
        [OpenApiParameter(
            QueryParams.RetrievedPensions.AssetId,
            ParameterLocation.Query,
            "Gets a specific pensions retrieval record with the specified Id.",
            false)]
        [SwaggerResponse((int)HttpStatusCode.OK, ContentTypes = ["application/json"], Type = typeof(RetrievedPensionRecord),
            Description = "The array of Retrieved Pension Records that match the provided query parameters")]
        [SwaggerResponse((int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> GetAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "retrieved-pension-records")] HttpRequest req)
        {
            var category = req.Query[QueryParams.RetrievedPensions.PensionCategory];
            var assetId = req.Query[QueryParams.RetrievedPensions.AssetId];

            return await ProcessRetrievedRecordsAsync(req, Constants.PensionsGetLogSource, id => repository.GetRetrievedRecordsAsync(id, category, assetId));
        }

        [Function("DeleteRetrievedRecords")]
        [SwaggerOperation(
            OperationId = "delete-pensions-retrieved-records-id",
            Summary = "Delete Pensions Retrieved Record",
            Description = "Deletes the given pension retrieved records given userSessionId")]
        [PensionDataOpenApi]
        [SwaggerResponse((int)HttpStatusCode.OK, ContentTypes = ["application/json"], Type = typeof(int),
            Description = "The number of records deleted as part of the request")]
        public async Task<IActionResult> DeleteAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "retrieved-pension-records")] HttpRequest req)
        {
            return await ProcessRetrievedRecordsAsync(req, Constants.PensionsDeleteLogSource, async userSessionId =>
            {
                await repository.DeleteRetrievedRecordsAsync(userSessionId);
                return default(int);
            });
        }

        [Function("GetStatus")]
        [SwaggerOperation(
        OperationId = "get-status",
        Summary = "Get Service Status",
        Description = "Gets the deployed version information of the service")]
        [SwaggerResponse((int)HttpStatusCode.OK, Description = "Status Data")]
        public async Task<IActionResult> GetStatusAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = HttpEndpoints.Internal.Status)] HttpRequest _)
        {
            var status = statusProvider.GetServiceStatus();

            return new OkObjectResult(status);
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

            var userSessionId = req.Headers[HeaderConstants.UserSessionId].ToString();

            logger.LogRequestReceived($"{logSource} for session Id: {userSessionId}");

            if (!validator.IsValidGuid(userSessionId))
            {
                logger.LogError("Unable to service request for session [{SessionId}]: {Reason}", userSessionId, Constants.InvalidSessionId);
                return new BadRequestObjectResult(Constants.InvalidSessionId);
            }

            var records = await processor(userSessionId);

            logger.LogResponseSent(records);

            return new OkObjectResult(records);
        }
    }
}
