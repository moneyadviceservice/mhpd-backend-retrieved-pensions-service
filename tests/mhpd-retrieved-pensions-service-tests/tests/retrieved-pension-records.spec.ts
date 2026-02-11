import { RetrievedPensionRecordsSchema } from 'schemas/retrievedPensionRecords.schema';
import { test, expect } from '../lib/test.lib';
import { v4 as uuid } from 'uuid';
import { setupPensionRecord } from 'utilities/helpers';

const iss = 'some-iss';

test.describe('GET - /retrieved-pension-records', () => {
  test('should return valid schema with successful request', async ({
    retrievedPensionService,
    pensionRetrievalService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const recordId = await setupPensionRecord(
      { pensionRetrievalService, pensionsDataService },
      sessionId,
      iss,
    );

    const response = await retrievedPensionService.getRetrievedPensionRecords(
      {
        userSessionId: sessionId,
        mhpdCorrelationId: sessionId,
      },
      recordId,
    );

    expect(response.status).toBe(200);

    const validation = RetrievedPensionRecordsSchema.safeParse(response.data);

    if (!validation.success) {
      console.error(
        '❌ Retrieved Pension Records Schema Validation Failed:',
        JSON.stringify(validation.error.issues, null, 2),
      );
    }

    expect(validation.success).toBe(true);
  });

  test('should return 200 with missing correlation id', async ({
    retrievedPensionService,
    pensionRetrievalService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const recordId = await setupPensionRecord(
      { pensionRetrievalService, pensionsDataService },
      sessionId,
      iss,
    );

    const response = await retrievedPensionService.getRetrievedPensionRecords(
      {
        userSessionId: sessionId,
        mhpdCorrelationId: '',
      },
      recordId,
    );

    expect(response.status).toBe(200);
  });

  test('should return 400 with invalid correlation id', async ({
    retrievedPensionService,
    pensionRetrievalService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const recordId = await setupPensionRecord(
      { pensionRetrievalService, pensionsDataService },
      sessionId,
      iss,
    );

    const response = await retrievedPensionService.getRetrievedPensionRecords(
      {
        userSessionId: sessionId,
        mhpdCorrelationId: 'invalid',
      },
      recordId,
    );

    expect(response.status).toBe(400);
  });

  test('should return 400 with missing user session id', async ({
    retrievedPensionService,
    pensionRetrievalService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const recordId = await setupPensionRecord(
      { pensionRetrievalService, pensionsDataService },
      sessionId,
      iss,
    );

    const response = await retrievedPensionService.getRetrievedPensionRecords(
      {
        userSessionId: '',
        mhpdCorrelationId: sessionId,
      },
      recordId,
    );

    expect(response.status).toBe(400);
  });

  test('should return 400 with invalid user session id', async ({
    retrievedPensionService,
    pensionRetrievalService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const recordId = await setupPensionRecord(
      { pensionRetrievalService, pensionsDataService },
      sessionId,
      iss,
    );

    const response = await retrievedPensionService.getRetrievedPensionRecords(
      {
        userSessionId: 'invalidid',
        mhpdCorrelationId: sessionId,
      },
      recordId,
    );

    expect(response.status).toBe(400);
  });
});

test.describe('DELETE - /retrieved-pension-records', () => {
  test('should return 200 with successful delete request', async ({
    retrievedPensionService,
    pensionRetrievalService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const recordId = await setupPensionRecord(
      { pensionRetrievalService, pensionsDataService },
      sessionId,
      iss,
    );

    const response = await retrievedPensionService.deleteRetrievedPensionRecords(
      {
        userSessionId: sessionId,
        mhpdCorrelationId: sessionId,
      },
      recordId,
    );

    expect(response.status).toBe(200);
  });

  test('should return 200 with missing correlation id', async ({
    retrievedPensionService,
    pensionRetrievalService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const recordId = await setupPensionRecord(
      { pensionRetrievalService, pensionsDataService },
      sessionId,
      iss,
    );

    const response = await retrievedPensionService.deleteRetrievedPensionRecords(
      {
        userSessionId: sessionId,
        mhpdCorrelationId: '',
      },
      recordId,
    );

    expect(response.status).toBe(200);
  });

  test('should return 400 with invalid correlation id', async ({
    retrievedPensionService,
    pensionRetrievalService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const recordId = await setupPensionRecord(
      { pensionRetrievalService, pensionsDataService },
      sessionId,
      iss,
    );

    const response = await retrievedPensionService.deleteRetrievedPensionRecords(
      {
        userSessionId: sessionId,
        mhpdCorrelationId: 'invalid',
      },
      recordId,
    );

    expect(response.status).toBe(400);
  });

  test('should return 400 with missing user session id', async ({
    retrievedPensionService,
    pensionRetrievalService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const recordId = await setupPensionRecord(
      { pensionRetrievalService, pensionsDataService },
      sessionId,
      iss,
    );

    const response = await retrievedPensionService.deleteRetrievedPensionRecords(
      {
        userSessionId: '',
        mhpdCorrelationId: sessionId,
      },
      recordId,
    );

    expect(response.status).toBe(400);
  });

  test('should return 400 with invalid user session id', async ({
    retrievedPensionService,
    pensionRetrievalService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const recordId = await setupPensionRecord(
      { pensionRetrievalService, pensionsDataService },
      sessionId,
      iss,
    );

    const response = await retrievedPensionService.deleteRetrievedPensionRecords(
      {
        userSessionId: 'invalidid',
        mhpdCorrelationId: sessionId,
      },
      recordId,
    );

    expect(response.status).toBe(400);
  });
});
