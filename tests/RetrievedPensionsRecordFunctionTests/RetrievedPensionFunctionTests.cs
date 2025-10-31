using Azure.Messaging.ServiceBus;
using MhpdCommon.Models.MessageBodyModels;
using MhpdCommon.Models.MHPDModels;
using MhpdCommon.Utils;
using MhpdCommon.ViewData;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Moq;
using RetrievedPensionsRecordFunction;
using RetrievedPensionsRecordFunction.Repository;
using RetrievedPensionsRecordFunctionTests.Data;
using static MhpdCommon.ViewData.EvaluationConstants;

namespace RetrievedPensionsRecordFunctionTests;

public class RetrievedPensionFunctionTests
{
    private readonly Mock<ILogger<RetrievedPensionsFunction>> _loggerMock;
    private readonly Mock<IIdValidator> _idValidatorMock;
    private readonly Mock<IArrangementProcessor> _processorMock;
    private readonly Mock<IPensionRecordRepository> _repositoryMock;
    private readonly Mock<ServiceBusMessageActions> _actionsMock;
    private readonly Mock<IMessageParser> _messageParseMock;
    private readonly RetrievedPensionsFunction _function;

    public RetrievedPensionFunctionTests()
    {
        _loggerMock = new Mock<ILogger<RetrievedPensionsFunction>>();

        _idValidatorMock = new Mock<IIdValidator>();
        _idValidatorMock.Setup(x => x.IsValidGuid(It.IsAny<string>())).Returns(false);
        _idValidatorMock.Setup(x => x.IsValidPeI(It.IsAny<string>())).Returns(false);

        _processorMock = new Mock<IArrangementProcessor>();
        _processorMock.Setup(x => x.ProcessArrangement(It.IsAny<string>())).Returns((string input) => input);

        _repositoryMock = new Mock<IPensionRecordRepository>();
        _repositoryMock.Setup(x => x.SaveRetrievedPensionRecordAsync(It.IsAny<string>(), It.IsAny<RetrievedPensionRecord>())).ReturnsAsync(true);

        _messageParseMock = new Mock<IMessageParser>();
        var error = new AggregateException(new Exception("Bad Data"));
        _messageParseMock.Setup(x => x.ToRetrievedPensionPayload(It.IsAny<string>())).Throws(error);

        _function = new RetrievedPensionsFunction(_loggerMock.Object, _idValidatorMock.Object, _messageParseMock.Object, _repositoryMock.Object, _processorMock.Object);

        _actionsMock = new Mock<ServiceBusMessageActions>();
        _actionsMock.Setup(x => x.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(),
            null, It.IsAny<string>(), null, It.IsAny<CancellationToken>())).Verifiable();
        _actionsMock.Setup(x => x.AbandonMessageAsync(It.IsAny<ServiceBusReceivedMessage>(),
            null, It.IsAny<CancellationToken>())).Verifiable();
        _actionsMock.Setup(x => x.CompleteMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>())).Verifiable();
    }

    [Fact]
    public async Task Run_ShouldCallDeadLetterQueue_OnNoCorrelationId()
    {
        ResetInvocations();

        //arrange
        _actionsMock.Invocations.Clear();
        _loggerMock.Invocations.Clear();
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData("Test message"));

        // Act
        await _function.Run(message, _actionsMock.Object);

        // Assert
        var reason = "Missing or Invalid correlationId:";
        _actionsMock.Verify(r => r.DeadLetterMessageAsync(message, null,
            It.Is<string>(arg => arg.StartsWith(reason)), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Run_ShouldSaveErrorRecord_OnPayloadParseFail()
    {
        ResetInvocations();

        //arrange
        _idValidatorMock.Setup(x => x.IsValidGuid(It.IsAny<string>())).Returns(true);

        var content = DataProvider.GetString("InvalidRecordIdPayload.json");
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(content), correlationId: Guid.NewGuid().ToString());

        // Act
        await _function.Run(message, _actionsMock.Object);

        // Assert
        _repositoryMock.Verify(x => x.SaveRetrievedPensionRecordAsync(It.IsAny<string>(), 
            It.Is<RetrievedPensionRecord>(rec => rec.Category == Category.Error)), Times.Once);
    }

    [Fact]
    public async Task Run_ShouldSaveErrorRecord_OnPayloadValidateFail()
    {
        ResetInvocations();

        //arrange
        const string file = "EmptyGuidRecordIdPayload.json";
        RetrievedPensionDetailsPayload? payload = null;
        _idValidatorMock.Setup(x => x.IsValidGuid(It.IsAny<string>())).Returns(true);
        _messageParseMock.Setup(x => x.ToRetrievedPensionPayload(It.IsAny<string>())).Returns(payload);

        var content = DataProvider.GetString(file);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(content), correlationId: Guid.NewGuid().ToString());

        // Act
        await _function.Run(message, _actionsMock.Object);

        // Assert
        _repositoryMock.Verify(x => x.SaveRetrievedPensionRecordAsync(It.IsAny<string>(),
            It.Is<RetrievedPensionRecord>(rec => rec.Category == Category.Error)), Times.Once);
    }

    [Fact]
    public async Task Run_ShouldCallAbandonMessage_OnSaveFail()
    {
        ResetInvocations();

        //arrange
        const string file = "ValidRetrievedPensionPayload.json";
        var payload = DataProvider.GetPayload(file);

        _idValidatorMock.Setup(x => x.IsValidGuid(It.IsAny<string>())).Returns(true);
        _messageParseMock.Setup(x => x.ToRetrievedPensionPayload(It.IsAny<string>())).Returns(payload);
        _repositoryMock.Setup(x => x.SaveRetrievedPensionRecordAsync(It.IsAny<string>(), It.IsAny<RetrievedPensionRecord>())).ReturnsAsync(false);

        var content = DataProvider.GetString(file);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(content), correlationId: Guid.NewGuid().ToString());

        // Act
        await _function.Run(message, _actionsMock.Object);

        // Assert
        _actionsMock.Verify(r => r.AbandonMessageAsync(message, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("ValidRetrievedPensionPayload.json", EvaluationConstants.Category.Contact, "1ba03e25-659a-43b8-ae77-b956df168969")]
    [InlineData("DB_ERI-DB_AP-NONE-Payload.json", EvaluationConstants.Category.Confirmed, "b057131c-d860-40db-b521-15e62a078128")]
    [InlineData("DC_ERI-NET_AP-ANO-Payload.json", EvaluationConstants.Category.Pending, "9f1bfd4a-4e39-4c59-bac5-c6860250f962")]
    [InlineData("DC_ERI-NONE-SML_AP-NONE-Payload.json", EvaluationConstants.Category.Confirmed, "89885682-d540-4abe-a075-bc25a46b79df")]
    public async Task Run_ShouldCallCompleteMessage_OnSaveSuccess(string file, string category, string assetId)
    {
        ResetInvocations();

        //arrange
        var payload = DataProvider.GetPayload(file);
        
        _idValidatorMock.Setup(x => x.IsValidGuid(It.IsAny<string>())).Returns(true);
        _messageParseMock.Setup(x => x.ToRetrievedPensionPayload(It.IsAny<string>())).Returns(payload);
        _repositoryMock.Setup(x => x.SaveRetrievedPensionRecordAsync(It.IsAny<string>(), It.IsAny<RetrievedPensionRecord>())).ReturnsAsync(true);

        var content = DataProvider.GetString(file);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(content), correlationId: Guid.NewGuid().ToString());

        // Act
        await _function.Run(message, _actionsMock.Object);

        // Assert
        _actionsMock.Verify(r => r.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveRetrievedPensionRecordAsync(
            It.Is<string>(id => id == message.CorrelationId),
            It.Is<RetrievedPensionRecord>(record =>
                record.Pei == payload!.Pei &&
                record.PensionsRetrievalRecordId == payload!.PensionRetrievalRecordId &&
                record.Category == category &&
                record.AssetId == assetId)), Times.Once);
    }

    [Fact]
    public async Task Run_ShouldCallCompleteMessage_OnSaveSuccessWithValidEscapedChars()
    {
        ResetInvocations();

        //arrange
        var payload = DataProvider.GetPayload("EscapableCharValidRetrievedPensionPayload.json");

        _idValidatorMock.Setup(x => x.IsValidGuid(It.IsAny<string>())).Returns(true);
        _messageParseMock.Setup(x => x.ToRetrievedPensionPayload(It.IsAny<string>())).Returns(payload);
        _repositoryMock.Setup(x => x.SaveRetrievedPensionRecordAsync(It.IsAny<string>(), It.IsAny<RetrievedPensionRecord>())).ReturnsAsync(true);

        var content = DataProvider.GetString("EscapableCharValidRetrievedPensionPayload.json");
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(content), correlationId: Guid.NewGuid().ToString());

        // Act
        await _function.Run(message, _actionsMock.Object);

        // Assert
        _actionsMock.Verify(r => r.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveRetrievedPensionRecordAsync(
            It.Is<string>(id => id == message.CorrelationId),
            It.Is<RetrievedPensionRecord>(record => !record.SchemeName.Contains('\\'))), Times.Once);
        
    }

    [Fact]
    public async Task Run_ShouldCallCompleteMessage_OnSysErrorPayload()
    {
        ResetInvocations();

        //arrange
        const string file = "SysErrorPayload.json";
        var payload = DataProvider.GetPayload(file);

        _idValidatorMock.Setup(x => x.IsValidGuid(It.IsAny<string>())).Returns(true);
        _messageParseMock.Setup(x => x.ToRetrievedPensionPayload(It.IsAny<string>())).Returns(payload);
        _repositoryMock.Setup(x => x.SaveRetrievedPensionRecordAsync(It.IsAny<string>(), It.IsAny<RetrievedPensionRecord>())).ReturnsAsync(true);

        var content = DataProvider.GetString(file);
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(content), correlationId: Guid.NewGuid().ToString());

        // Act
        await _function.Run(message, _actionsMock.Object);

        // Assert
        _actionsMock.Verify(r => r.CompleteMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveRetrievedPensionRecordAsync(
            It.Is<string>(id => id == message.CorrelationId),
            It.Is<RetrievedPensionRecord>(record =>
                record.Pei == payload!.Pei &&
                record.PensionsRetrievalRecordId == payload!.PensionRetrievalRecordId &&
                record.Category == EvaluationConstants.Category.Error && 
                record.PensionType == EvaluationConstants.Category.Error &&
                record.MatchType == EvaluationConstants.Category.Error)), Times.Once);
    }

    private void ResetInvocations()
    {
        _actionsMock.Invocations.Clear();
    }
}
