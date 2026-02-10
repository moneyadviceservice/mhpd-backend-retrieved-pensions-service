import { z } from 'zod';

const RetrievalResultSchema = z.object({
  externalAssetId: z.uuid(),
  schemeName: z.string(),
  pensionCategory: z.string(),
  matchType: z.string().optional(),
  retirementDate: z.string().nullable().optional(),
  dateOfBirth: z.string().nullable().optional(),
  pensionType: z.string().nullable().optional(),
  pensionStatus: z.string().nullable().optional(),
  contactReference: z.string().nullable().optional(),
  startDate: z.string().nullable().optional(),
  benefitIllustrations: z.array(z.any()).optional(),
  pensionAdministrator: z.any().optional(),
  employmentMembershipPeriods: z.array(z.any()).optional(),
});

const RetrievedPensionRecordSchema = z.object({
  id: z.uuid(),
  correlationId: z.uuid(),
  pei: z.string(),
  userSessionId: z.uuid(),
  pensionType: z.string(),
  matchType: z.string(),
  assetId: z.uuid(),
  category: z.string(),
  schemeName: z.string(),
  hasIncome: z.string(),
  administratorName: z.string(),
  pensionLinkId: z.string().nullable(),
  retrievalResult: RetrievalResultSchema,
});

export const RetrievedPensionRecordsSchema = z.array(RetrievedPensionRecordSchema);

export type RetrievedPensionRecords = z.infer<typeof RetrievedPensionRecordsSchema>;
