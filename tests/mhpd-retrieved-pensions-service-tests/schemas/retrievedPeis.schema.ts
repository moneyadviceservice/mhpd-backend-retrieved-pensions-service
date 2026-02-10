import { z } from 'zod';

const uuidRegex = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';

export const PeisEntrySchema = z.string().regex(new RegExp(`^${uuidRegex}:${uuidRegex}$`, 'i'), {
  message: "Invalid PEIS format. Expected 'UUID:UUID'.",
});

export const RetrievedPeisSchema = z.array(PeisEntrySchema);

export type RetrievedPeis = z.infer<typeof RetrievedPeisSchema>;
