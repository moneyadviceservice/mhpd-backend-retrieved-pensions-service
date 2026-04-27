using MhpdCommon.Models.MHPDModels;
using MhpdCommon.Repository;
using Microsoft.Extensions.Logging;
using Moq;
using RetrievedPensionsRecordFunction.Repository;
using RetrievedPensionsRecordFunctionTests.Data;

namespace RetrievedPensionsRecordFunctionTests;

public  class PensionRecordRepositoryTests
{
    private readonly PensionRecordRepository _repository;
    private readonly Mock<IRetrievedPensionRecordRedisRepository> _mockRetrievedPensionRecordRedisRepository;

    public PensionRecordRepositoryTests()
    {
        var loggerMock = new Mock<ILogger<PensionRecordRepository>>();
        _mockRetrievedPensionRecordRedisRepository = new Mock<IRetrievedPensionRecordRedisRepository>();
        _repository = new PensionRecordRepository(loggerMock.Object, _mockRetrievedPensionRecordRedisRepository.Object);
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
    public async Task WhenSessionRecordsAreRequested_DatabaseResultIsCorrect()
    {
        //Arrange
        List<RetrievedPensionRecord> records = [
            new RetrievedPensionRecord {
                UserSessionId = "sessionId",
                Category = "CONFIRMED"
            },
            new RetrievedPensionRecord {
                UserSessionId = "sessionId",
                Category = "CONFIRMED"
            },
        ];

        _mockRetrievedPensionRecordRedisRepository.Setup(r => r.GetAllByUserSessionIdAsync("sessionId")).ReturnsAsync(records);

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
            ? new RetrievedPensionRecord { AssetId = "A", PensionLinkId = "XYZ", Category = "CONFIRMED" }
            : new RetrievedPensionRecord { AssetId = "B", Category = "CONFIRMED" };

        List<RetrievedPensionRecord> detailRecord = [ retrievedPensionRecord ];

        List<RetrievedPensionRecord> allRecords = [
            new RetrievedPensionRecord{ AssetId = "A", PensionLinkId = "XYZ", Category = "CONFIRMED"},
            new RetrievedPensionRecord{ AssetId = "B", Category = "CONFIRMED"},
            new RetrievedPensionRecord{ AssetId = "C", PensionLinkId = "XYZ", Category = "CONFIRMED"}
        ];

        _mockRetrievedPensionRecordRedisRepository.Setup(r => r.GetAllByUserSessionIdAsync("sessionId")).ReturnsAsync(allRecords);

        //Act
        var result = await _repository.GetRetrievedRecordsAsync("sessionId", "CONFIRMED", retrievedPensionRecord.AssetId);

        //Assert
        Assert.Equal(expectedCount, result.Count);
    }

    [Fact]
    public async Task WhenPeiIsRequested_DatabaseResultIsCorrect()
    {
        //Arrange
        var userSessionId = Guid.NewGuid().ToString();
        List<RetrievedPensionRecord> records = [
            new RetrievedPensionRecord{ Pei = "A"},
            new RetrievedPensionRecord{ Pei = "B"},
            new RetrievedPensionRecord{ Pei = "C"}
        ];
        _mockRetrievedPensionRecordRedisRepository.Setup(r => r.GetAllByUserSessionIdAsync(userSessionId)).ReturnsAsync(records);

        //Act
        var result = await _repository.GetRetrievedPeisAsync(userSessionId);

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
        var userSessionId = Guid.NewGuid().ToString();

        //Act
        await _repository.DeleteRetrievedRecordsAsync(userSessionId);

        //Assert
        _mockRetrievedPensionRecordRedisRepository.Verify(c => c.DeleteByIdUserSessionIdAsync(userSessionId), Times.Once);
    }

    private static RetrievedPensionRecord GetPayload()
    {
        return new RetrievedPensionRecord
        {
            AssetId = Guid.NewGuid().ToString(),
            Pei = "pei",
            UserSessionId = "sessionId",
            RetrievalResult = Array.Empty<List<PensionArrangement>>()
        };
    }
}
