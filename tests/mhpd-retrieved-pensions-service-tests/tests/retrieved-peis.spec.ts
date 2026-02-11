import { test, expect } from '@lib/test.lib';
import { v4 as uuid } from 'uuid';
import { RetrievedPeisSchema } from 'schemas/retrievedPeis.schema';
import { setupPensionRecord } from 'utilities/helpers';

const iss = 'some-iss';

test.describe('GET - /retrieved-peis', () => {
  test('should return 200 with successful request', async ({
    pensionsDataService,
    pensionRetrievalService,
    retrievedPensionService,
  }) => {
    const sessionId = uuid();

    await setupPensionRecord({ pensionRetrievalService, pensionsDataService }, sessionId, iss);

    const response = await retrievedPensionService.getPeis({
      userSessionId: sessionId,
      mhpdCorrelationId: sessionId,
    });

    expect(response.status).toBe(200);

    const validation = RetrievedPeisSchema.safeParse(response.data);

    if (!validation.success) {
      console.error(
        '❌ Retrieved PEIS Schema Validation Failed:',
        JSON.stringify(validation.error.issues, null, 2),
      );
    }

    expect(validation.success).toBe(true);
  });

  test('should return 200 with missing correlation id', async ({
    pensionsDataService,
    pensionRetrievalService,
    retrievedPensionService,
  }) => {
    const sessionId = uuid();

    await setupPensionRecord({ pensionRetrievalService, pensionsDataService }, sessionId, iss);

    const response = await retrievedPensionService.getPeis({
      userSessionId: sessionId,
      mhpdCorrelationId: '',
    });

    expect(response.status).toBe(200);
  });

  test('should return 400 with invalid correlation id', async ({
    pensionsDataService,
    pensionRetrievalService,
    retrievedPensionService,
  }) => {
    const sessionId = uuid();

    await setupPensionRecord({ pensionRetrievalService, pensionsDataService }, sessionId, iss);

    const response = await retrievedPensionService.getPeis({
      userSessionId: sessionId,
      mhpdCorrelationId: 'invalid',
    });

    expect(response.status).toBe(400);
  });

  test('should return 400 with missing user session id', async ({
    pensionsDataService,
    pensionRetrievalService,
    retrievedPensionService,
  }) => {
    const sessionId = uuid();

    await setupPensionRecord({ pensionRetrievalService, pensionsDataService }, sessionId, iss);

    const response = await retrievedPensionService.getPeis({
      userSessionId: '',
      mhpdCorrelationId: sessionId,
    });

    expect(response.status).toBe(400);
  });

  test('should return 400 with invalid user session id', async ({
    pensionsDataService,
    pensionRetrievalService,
    retrievedPensionService,
  }) => {
    const sessionId = uuid();

    await setupPensionRecord({ pensionRetrievalService, pensionsDataService }, sessionId, iss);

    const response = await retrievedPensionService.getPeis({
      userSessionId: 'invalidid',
      mhpdCorrelationId: sessionId,
    });

    expect(response.status).toBe(400);
  });
});
