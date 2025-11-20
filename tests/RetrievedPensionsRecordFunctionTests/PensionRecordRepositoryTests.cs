using MhpdCommon.Models.Configuration;
using MhpdCommon.Models.MHPDModels;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RetrievedPensionsRecordFunction.Repository;
using RetrievedPensionsRecordFunctionTests.Data;
using System.Net;

namespace RetrievedPensionsRecordFunctionTests;

public  class PensionRecordRepositoryTests
{
    private readonly Mock<ItemResponse<RetrievedPensionRecord>> _writeResponse;
    private readonly Mock<FeedResponse<RetrievedPensionRecord>> _readResponse;
    private readonly PensionRecordRepository _repository;

    public PensionRecordRepositoryTests()
    {
        var configuration = new CosmosBusinessConfiguration
        {
            DatabaseId = "PensionDatabase",
            RetrievedPensionsContainer = "PensionContainer"
        };

        var container = new Mock<Container>();
        var client = new Mock<CosmosClient>();
        var iterator = new Mock<FeedIterator<RetrievedPensionRecord>>();
        var loggerMock = new Mock<ILogger<PensionRecordRepository>>();

        _writeResponse = new Mock<ItemResponse<RetrievedPensionRecord>>();
        _readResponse = new Mock<FeedResponse<RetrievedPensionRecord>>();

        iterator.Setup(mock => mock.ReadNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_readResponse.Object);

        client.Setup(mock => mock.GetContainer(configuration.DatabaseId, configuration.RetrievedPensionsContainer))
            .Returns(container.Object);

        container.Setup(mock => mock.UpsertItemAsync(
            It.IsAny<RetrievedPensionRecord>(), It.IsAny<PartitionKey>(), null, default))
            .Returns(Task.FromResult(_writeResponse.Object));
        container.Setup(mock => mock.GetItemQueryIterator<RetrievedPensionRecord>(It.IsAny<QueryDefinition>(),
            It.IsAny<string>(), It.IsAny<QueryRequestOptions>())).Returns(iterator.Object);

        var options = Options.Create(configuration);
        _repository = new PensionRecordRepository(client.Object, options, loggerMock.Object);
    }

    [Fact]
    public async Task WhenNewPayloadIsProvided_NewRecordIsSaved()
    {
        //Arrange
        var payload = GetPayload();
        _writeResponse.Setup(r => r.StatusCode).Returns(HttpStatusCode.Created);

        //Act
        var result = await _repository.SaveRetrievedPensionRecordAsync("CorrelationId", payload);

        //Assert
        Assert.True(result);
    }

    [Fact]
    public async Task WhenNoCorrelationIdIsProvided_NewRecordIsNotSaved()
    {
        //Arrange
        var payload = GetPayload();

        //Act
        var result = await _repository.SaveRetrievedPensionRecordAsync("            ", payload);

        //Assert
        Assert.False(result);
    }

    [Fact]
    public async Task WhenExistingPayloadIsProvided_RecordIsUpdated()
    {
        //Arrange
        var payload = GetPayload();
        _writeResponse.Setup(r => r.StatusCode).Returns(HttpStatusCode.OK);

        //Act
        var result = await _repository.SaveRetrievedPensionRecordAsync("CorrelationId", payload);

        //Assert
        Assert.True(result);
    }

    [Fact]
    public async Task WhenClientDoesNotSave_ResponseReturnsFalse()
    {
        //Arrange
        var payload = GetPayload();
        _writeResponse.Setup(r => r.StatusCode).Returns(HttpStatusCode.BadRequest);

        //Act
        var result = await _repository.SaveRetrievedPensionRecordAsync("CorrelationId", payload);

        //Assert
        Assert.False(result);
    }

    [Fact]
    public async Task WhenSessionRecordsAreRequested_DatabaseResultIsCorrect()
    {
        //Arrange
        List<RetrievedPensionRecord> records = [
            new RetrievedPensionRecord(),
            new RetrievedPensionRecord()
        ];

        _readResponse.Setup(mock => mock.GetEnumerator()).Returns(records.GetEnumerator);

        //Act
        var result = await _repository.GetRetrievedRecordsAsync("sessionId", "CONFIRMED");

        //Assert
        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task WhenDetailRecordIsRequested_DatabaseResultIsCorrect(bool withPensionLink, int expectedCount)
    {
        //Arrange
        RetrievedPensionRecord retrievedPensionRecord = withPensionLink
            ? new RetrievedPensionRecord { AssetId = "A", PensionLinkId = "XYZ" }
            : new RetrievedPensionRecord { AssetId = "B" };

        List<RetrievedPensionRecord> detailRecord = [ retrievedPensionRecord ];

        List<RetrievedPensionRecord> allRecords = [
            new RetrievedPensionRecord{ AssetId = "A", PensionLinkId = "XYZ"},
            new RetrievedPensionRecord{ AssetId = "B"},
            new RetrievedPensionRecord{ AssetId = "C", PensionLinkId = "XYZ"}
        ];

        _readResponse
        .SetupSequence(r => r.GetEnumerator())
        .Returns(detailRecord.GetEnumerator())
        .Returns(allRecords.GetEnumerator());

        //Act
        var result = await _repository.GetRetrievedRecordsAsync("sessionId", "CONFIRMED", retrievedPensionRecord.AssetId);

        //Assert
        Assert.Equal(expectedCount, result.Count);
    }

    [Fact]
    public async Task WhenPeiIsRequested_DatabaseResultIsCorrect()
    {
        //Arrange
        List<RetrievedPensionRecord> records = [
            new RetrievedPensionRecord{ Pei = "A"},
            new RetrievedPensionRecord{ Pei = "B"},
            new RetrievedPensionRecord{ Pei = "C"}
        ];

        _readResponse.Setup(mock => mock.GetEnumerator()).Returns(records.GetEnumerator);
        _readResponse.Setup(mock => mock.Count).Returns(records.Count);

        //Act
        var result = await _repository.GetRetrievedPeisAsync(Guid.NewGuid().ToString());

        //Assert
        Assert.Equal(records.Count, result.Count);
        Assert.Contains("A", result);
        Assert.Contains("B", result);
        Assert.Contains("C", result);
    }

    [Fact]
    public async Task WhenRecordAreDeleted_DatabaseResultIsCorrect()
    {
        //Arrange
        List<RetrievedPensionRecord> records = [
            new RetrievedPensionRecord(),
            new RetrievedPensionRecord(),
            new RetrievedPensionRecord()
        ];

        _readResponse.Setup(mock => mock.GetEnumerator()).Returns(records.GetEnumerator);
        _readResponse.Setup(mock => mock.Count).Returns(records.Count);

        //Act
        var result = await _repository.DeleteRetrievedRecordsAsync(Guid.NewGuid().ToString());

        //Assert
        Assert.Equal(records.Count, result);
    }

    private static RetrievedPensionRecord GetPayload()
    {
        return new RetrievedPensionRecord
        {
            Pei = "pei",
            UserSessionId = "sessionId",
            RetrievalResult = Array.Empty<List<PensionArrangement>>()
        };
    }
}
