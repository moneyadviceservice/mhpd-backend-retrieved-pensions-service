import { env } from '@lib/env.lib'; // Use your validated lib
import { PensionRetrievalService } from '../services/pension-retrieval-service';
import { PensionsDataService } from '../services/pensions-data-service';

export async function pollForPensionRecord(
  service: PensionRetrievalService,
  headers: { userSessionId: string; mhpdCorrelationId: string; iss: string },
  maxAttempts = 10,
  interval = 2000,
) {
  let attempts = 0;

  while (attempts < maxAttempts) {
    const response = await service.getPensionsRetrievalRecords(headers);

    if (response.status === 200 && response.data?.userSessionId) {
      return response.data.userSessionId;
    }

    console.log(
      `[Polling] Record not ready. Attempt ${String(attempts + 1)}/${String(maxAttempts)}. Waiting ${String(interval)}ms...`,
    );

    await new Promise((res) => setTimeout(res, interval));
    attempts++;
  }

  throw new Error(`Exceeded max polling attempts (${String(maxAttempts)}). Record was never ready.`);
}

export async function setupPensionRecord(
  {
    pensionsDataService,
    pensionRetrievalService,
  }: {
    pensionsDataService: PensionsDataService;
    pensionRetrievalService: PensionRetrievalService;
  },
  sessionId: string,
  iss: string,
) {
  const pdsCsrfResponse = await pensionsDataService.getCSRFToken();
  const pdsCsrfToken = pdsCsrfResponse.cookies.get('X-XSRF-TOKEN');

  if (!pdsCsrfToken) {
    throw new Error('Failed to retrieve CSRF token from PensionsDataService');
  }

  const headers = {
    userSessionId: sessionId,
    iss,
    mhpdCorrelationId: sessionId,
    'X-XSRF-TOKEN': pdsCsrfToken,
  };

  await pensionsDataService.postPensionsData(headers, {
    clientId: env.CLIENT_ID,
    clientSecret: env.CLIENT_SECRET,
    authorisationCode: env.AUTHORISATION_CODE,
    redirectUrl: env.REDIRECT_URL,
    codeVerifier: env.CODE_VERIFIER,
  });

  await pensionsDataService.postPensionsDataRetrieval(headers, {
    ticket: env.TICKET,
    clientId: env.CLIENT_ID,
  });

  const recordId = await pollForPensionRecord(pensionRetrievalService, {
    userSessionId: sessionId,
    mhpdCorrelationId: sessionId,
    iss,
  });

  return recordId;
}
