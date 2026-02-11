import { APIClient } from '../lib/api.lib';
import { type APIRequestContext } from '@playwright/test';
import { RetrievedPensionRecords } from 'schemas/retrievedPensionRecords.schema';
import { RetrievedPeis } from 'schemas/retrievedPeis.schema';
import { env } from '@lib/env.lib';

interface Headers {
  userSessionId: string;
  mhpdCorrelationId: string;
}

export class RetrievedPensionService {
  protected readonly apiClient: APIClient;
  protected readonly baseURL = env.BASE_URL;

  constructor(request: APIRequestContext) {
    this.apiClient = new APIClient(request, this.baseURL);
  }

  async getRetrievedPensionRecords(headers: Headers, pensionsRetrievalRecordId?: string) {
    const params: Record<string, string> = {};
    if (pensionsRetrievalRecordId) {
      params.pensionsRetrievalRecordId = pensionsRetrievalRecordId;
    }

    return this.apiClient.get<RetrievedPensionRecords>('/retrieved-pension-records', {
      headers: headers as unknown as Record<string, string>,
      params,
    });
  }

  async deleteRetrievedPensionRecords(headers: Headers, pensionsRetrievalRecordId?: string) {
    const params: Record<string, string> = {};
    if (pensionsRetrievalRecordId) {
      params.pensionsRetrievalRecordId = pensionsRetrievalRecordId;
    }

    return this.apiClient.delete('/retrieved-pension-records', {
      headers: headers as unknown as Record<string, string>,
      params,
    });
  }

  async getPeis(headers: Headers) {
    return this.apiClient.get<RetrievedPeis>('/retrieved-peis', {
      headers: headers as unknown as Record<string, string>,
    });
  }
}
