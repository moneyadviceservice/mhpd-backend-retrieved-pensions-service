import { test as baseTest, expect as baseExpect } from '@playwright/test';
import { RetrievedPensionService } from '@services/retrieved-pension-service';
import { PensionRetrievalService } from '@services/pension-retrieval-service';
import { PensionsDataService } from '@services/pensions-data-service';

interface TestFixtures {
  retrievedPensionService: RetrievedPensionService;
  pensionRetrievalService: PensionRetrievalService;
  pensionsDataService: PensionsDataService;
}

export const test = baseTest.extend<TestFixtures>({
  retrievedPensionService: async ({ request }, use) => {
    const retrievedPensionService = new RetrievedPensionService(request);
    await use(retrievedPensionService);
  },

  pensionRetrievalService: async ({ request }, use) => {
    const pensionRetrievalService = new PensionRetrievalService(request);
    await use(pensionRetrievalService);
  },

  pensionsDataService: async ({ request }, use) => {
    const pensionsDataService = new PensionsDataService(request);
    await use(pensionsDataService);
  },
});

export const expect = baseExpect;
