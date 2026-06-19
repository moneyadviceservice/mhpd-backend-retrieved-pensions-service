using MhpdCommon.Constants;
using MhpdCommon.Constants.HttpClient;
using MhpdCommon.Models.MHPDModels;
using MhpdCommon.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using RetrievedPensionsRecordFunction;
using RetrievedPensionsRecordFunction.Models;
using RetrievedPensionsRecordFunction.Repository;
using System.Net;

namespace RetrievedPensionsRecordFunctionTests;

public class RetrievedRecordsFunctionTest
{
    private readonly Mock<IIdValidator> _idValidatorMock;
    private readonly Mock<ILogger<RetrievedRecordsFunction>> _loggerMock;
    private readonly Mock<IPensionRecordRepository> _repository;
    private readonly Mock<IServiceStatusProvider> _serviceStatusProviderMock;
    private readonly RetrievedRecordsFunction _function;

    public RetrievedRecordsFunctionTest()
    {
        _idValidatorMock = new Mock<IIdValidator>();
        _idValidatorMock.Setup(x => x.IsValidGuid(It.IsAny<string>())).Returns(true);

        _loggerMock = new Mock<ILogger<RetrievedRecordsFunction>>();

        _repository = new Mock<IPensionRecordRepository>();
        _repository.Setup(mock => mock.GetRetrievedRecordsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([new RetrievedPensionRecord()])
            .Verifiable();
        _repository.Setup(mock => mock.GetRetrievedPeisAsync(It.IsAny<string>()))
            .ReturnsAsync(["A", "B", "C"])
            .Verifiable();
        _repository.Setup(mock => mock.DeleteRetrievedRecordsAsync(It.IsAny<string>()))
            .Verifiable();

        _serviceStatusProviderMock = new Mock<IServiceStatusProvider>();

        _function = new RetrievedRecordsFunction(_loggerMock.Object, _repository.Object, 
            _idValidatorMock.Object, _serviceStatusProviderMock.Object);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Function_ShouldReturnOk_WhenHeadersAreValid(bool withCorrelationId)
    {
        //Arrange
        var userSessionId = Guid.NewGuid().ToString();
        var category = "Contact";
        var assetId = "1ba03e25-659a-43b8-ae77-b956df168969";
        var queryParams = new Dictionary<string, StringValues>
        {
            { QueryParams.RetrievedPensions.PensionCategory, category },
            { QueryParams.RetrievedPensions.AssetId, assetId }
        };

        var headers = new Dictionary<string, StringValues>
        {
            { HeaderConstants.UserSessionId, userSessionId }
        };

        if (withCorrelationId)
        {
            headers.Add(HeaderConstants.CorrelationId, Guid.NewGuid().ToString());
            
        }

        var mockRequest = new Mock<HttpRequest>();
        mockRequest.Setup(req => req.Query).Returns(new QueryCollection(queryParams));
        mockRequest.Setup(req => req.Headers).Returns(new HeaderDictionary(headers));

        //Act
        var response = await _function.GetAsync(mockRequest.Object);

        //Assert
        var result = Assert.IsType<OkObjectResult>(response);
        Assert.Equal((int)HttpStatusCode.OK, result.StatusCode);
        Assert.IsType<List<RetrievedPensionRecord>>(result.Value);
        _repository.Verify(mock => mock.GetRetrievedRecordsAsync(userSessionId, category, assetId), Times.Once);
    }

    [Fact]
    public async Task GetPeis_ShouldReturnOk_WhenHeadersAreValid()
    {
        //Arrange
        var userSessionId = Guid.NewGuid().ToString();
        var category = "Contact";
        var assetId = "1ba03e25-659a-43b8-ae77-b956df168969";
        var queryParams = new Dictionary<string, StringValues>
        {
            { QueryParams.RetrievedPensions.PensionCategory, category },
            { QueryParams.RetrievedPensions.AssetId, assetId }
        };

        var headers = new Dictionary<string, StringValues>
        {
            { HeaderConstants.CorrelationId, Guid.NewGuid().ToString() },
            { HeaderConstants.UserSessionId, userSessionId }
        };

        var mockRequest = new Mock<HttpRequest>();
        mockRequest.Setup(req => req.Query).Returns(new QueryCollection(queryParams));
        mockRequest.Setup(req => req.Headers).Returns(new HeaderDictionary(headers));

        //Act
        var response = await _function.GetPeisAsync(mockRequest.Object);

        //Assert
        var result = Assert.IsType<OkObjectResult>(response);
        Assert.Equal((int)HttpStatusCode.OK, result.StatusCode);
        Assert.IsType<List<string>>(result.Value);
        _repository.Verify(mock => mock.GetRetrievedPeisAsync(userSessionId), Times.Once);
    }

    [Fact]
    public async Task Function_ShouldReturnBadRequest_WhenQueryIsInvalid()
    {
        //Arrange
        var correlationId = Guid.NewGuid().ToString();
        var queryParams = new Dictionary<string, StringValues>();

        _idValidatorMock.Setup(x => x.IsValidGuid(It.IsAny<string>())).Returns(false);
        _idValidatorMock.Setup(x => x.IsValidGuid(correlationId)).Returns(true);
        
        var headers = new Dictionary<string, StringValues>
        {
            { HeaderConstants.CorrelationId, correlationId},
            { HeaderConstants.UserSessionId, Guid.NewGuid().ToString() }
        };

        var queries = new QueryCollection(queryParams);
        var mockRequest = new Mock<HttpRequest>();
        mockRequest.Setup(req => req.Query).Returns(queries);
        mockRequest.Setup(req => req.Headers).Returns(new HeaderDictionary(headers));

        //Act
        var response = await _function.GetAsync(mockRequest.Object);

        //Assert
        var result = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(Constants.InvalidSessionId, result.Value);
        _repository.Verify(mock => mock.GetRetrievedRecordsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Function_ShouldReturnBadRequest_WhenHeaderIsInvalid()
    {
        //Arrange
        _idValidatorMock.Setup(x => x.IsValidGuid(It.IsAny<string>())).Returns(false);

        var queryParams = new Dictionary<string, StringValues>
        {
            { QueryParams.RetrievedPensions.AssetId, Guid.NewGuid().ToString() }
        };

        var headers = new Dictionary<string, StringValues>
        {
            { HeaderConstants.CorrelationId, "Guid.NewGuid().ToString()"},
            { HeaderConstants.UserSessionId, "Guid.NewGuid().ToString()" }
        };

        var mockRequest = new Mock<HttpRequest>();
        mockRequest.Setup(req => req.Query).Returns(new QueryCollection(queryParams));
        mockRequest.Setup(req => req.Headers).Returns(new HeaderDictionary(headers));

        //Act
        var response = await _function.GetAsync(mockRequest.Object);

        //Assert
        var result = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(Constants.InvalidCorrelationId, result.Value);
        _repository.Verify(mock => mock.GetRetrievedRecordsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_ShouldReturnOk_WhenPayloadIsValid(bool withCorrelationId)
    {
        //Arrange
        var userSessionId = Guid.NewGuid().ToString();
        var headers = new Dictionary<string, StringValues>
        {
            { HeaderConstants.UserSessionId, userSessionId }
        };

        if (withCorrelationId)
        {
            headers.Add(HeaderConstants.CorrelationId, Guid.NewGuid().ToString());
            
        }

        var mockRequest = new Mock<HttpRequest>();
        mockRequest.Setup(req => req.Headers).Returns(new HeaderDictionary(headers));

        //Act
        var response = await _function.DeleteAsync(mockRequest.Object);

        //Assert
        var result = Assert.IsType<OkObjectResult>(response);
        Assert.Equal((int)HttpStatusCode.OK, result.StatusCode);
        Assert.IsType<int>(result.Value);
        _repository.Verify(mock => mock.DeleteRetrievedRecordsAsync(userSessionId), Times.Once);
    }

    [Fact]
    public async Task Function_ShouldReturnServiceStatus()
    {
        //Arrange
        var status = new ServiceStatus
        {
            ServiceName = "RetrievedPensionService"
        };

        _serviceStatusProviderMock.Setup(mock => mock.GetServiceStatus()).Returns(status);

        var mockRequest = new Mock<HttpRequest>();

        //Act
        var response = await _function.GetStatusAsync(mockRequest.Object);

        //Assert
        Assert.IsType<OkObjectResult>(response);
        _serviceStatusProviderMock.Verify(mock => mock.GetServiceStatus(), Times.Once);
    }
}
