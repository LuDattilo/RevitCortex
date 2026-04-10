import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const GetRoomOpeningsInput = z.object({
  roomIds: z
    .array(z.number())
    .optional()
    .describe("Room ElementIds to query. Omit for all rooms."),
  roomNumbers: z
    .array(z.string())
    .optional()
    .describe("Room numbers to query (alternative to roomIds)."),
  levelName: z
    .string()
    .optional()
    .describe("Filter rooms by level name (partial match)."),
  elementType: z
    .enum(["doors", "windows", "both"])
    .optional()
    .default("both")
    .describe("Type of openings to find. Default: both"),
  includeRoomParams: z
    .boolean()
    .optional()
    .default(false)
    .describe("Include room parameters. Default: false"),
  includeElementParams: z
    .boolean()
    .optional()
    .default(false)
    .describe("Include door/window parameters. Default: false"),
  parameterNames: z
    .array(z.string())
    .optional()
    .describe("Specific parameter names to extract (empty = all key parameters)"),
  maxElementsPerRoom: z
    .number()
    .int()
    .optional()
    .default(100)
    .describe("Max doors/windows per room. Default: 100"),
});

export function registerGetRoomOpeningsTool(server: McpServer): void {
  server.tool(
    "get_room_openings",
    "Find doors and/or windows in rooms with dimensions and room association. Phase-aware lookup via FromRoom/ToRoom.",
    GetRoomOpeningsInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("get_room_openings", args);
        });
        return toolResponse("get_room_openings", result, Date.now() - start, args);
      } catch (error) {
        return toolError("get_room_openings", error, Date.now() - start);
      }
    }
  );
}
