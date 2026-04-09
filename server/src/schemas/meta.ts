import { z } from "zod";

export const SayHelloInput = z.object({
  message: z
    .string()
    .optional()
    .describe("Custom greeting message. Defaults to 'Hello from RevitCortex!'"),
});
