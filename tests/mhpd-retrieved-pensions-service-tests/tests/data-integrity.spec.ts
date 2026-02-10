import { test, expect } from '@lib/test.lib';
import { v4 as uuid } from 'uuid';
import { setupPensionRecord } from '../utilities/helpers'; // Use relative or aliased path

const iss = 'mhpdIss';

test.describe('Cross-Service Data Integrity', () => {
  test('Triple-Handshake: Symmetry between Retrieval, PEIs, and Full Records', async ({
    pensionRetrievalService,
    retrievedPensionService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const headers = { userSessionId: sessionId, mhpdCorrelationId: sessionId, iss };

    const recordId = await setupPensionRecord(
      { pensionsDataService, pensionRetrievalService },
      sessionId,
      iss,
    );

    let checklistPeis: string[] = [];
    let expectedCount = 0;
    let attempts = 0;

    while (attempts < 15) {
      const res = await pensionRetrievalService.getPensionsRetrievalRecords(headers);

      if (res.data?.peiData && res.data.peiData.length > 0) {
        checklistPeis = res.data.peiData.map((p) => p.pei).sort();
        expectedCount = checklistPeis.length;
        break;
      }
      await new Promise((r) => setTimeout(r, 2000));
      attempts++;
    }

    let identityPeis: string[] = [];
    attempts = 0;
    while (attempts < 15) {
      const peisRes = await retrievedPensionService.getPeis(headers);
      if (peisRes.data?.length === expectedCount) {
        identityPeis = [...peisRes.data].sort();
        break;
      }
      console.log(
        `[Polling Symmetry] Vault has ${String(peisRes.data?.length ?? 0)}/${String(expectedCount)} records. Waiting...`,
      );
      await new Promise((r) => setTimeout(r, 2000));
      attempts++;
    }

    const recordsRes = await retrievedPensionService.getRetrievedPensionRecords(headers, recordId);
    const vaultPeis = (recordsRes.data ?? []).map((p) => p.pei).sort();

    expect(identityPeis).toEqual(checklistPeis);
    expect(vaultPeis).toEqual(checklistPeis);

    const samplePei = checklistPeis[0];
    const fullRecord = (recordsRes.data ?? []).find((r) => r.pei === samplePei);

    if (!fullRecord) {
      throw new Error(`Integrity Failure: PEI ${samplePei} missing in full records.`);
    }

    const expectedAssetId = samplePei.split(':')[1];
    expect(fullRecord.assetId).toBe(expectedAssetId);
    expect(fullRecord.retrievalResult.externalAssetId).toBe(expectedAssetId);
    expect(fullRecord.correlationId).toBe(headers.mhpdCorrelationId);
  });

  test.skip('Integrity: Data Purge - Deleting Records clears the PEI View', async ({
    pensionRetrievalService,
    retrievedPensionService,
    pensionsDataService,
  }) => {
    const sessionId = uuid();
    const headers = { userSessionId: sessionId, mhpdCorrelationId: sessionId, iss };

    const recordId = await setupPensionRecord(
      { pensionRetrievalService, pensionsDataService },
      sessionId,
      iss,
    );

    const initialPeis = await retrievedPensionService.getPeis(headers);
    expect(initialPeis.data?.length).toBeGreaterThan(0);

    await pensionRetrievalService.deletePensionsRetrievalRecords(headers);
    await retrievedPensionService.deleteRetrievedPensionRecords(headers, recordId);

    const postDeletePeis = await retrievedPensionService.getPeis(headers);
    const peiCount = postDeletePeis.data?.length ?? 0;

    expect(peiCount).toBe(0);
  });
});
