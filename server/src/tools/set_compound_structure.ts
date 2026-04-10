import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../connection/ConnectionManager.js";
import { toolResponse, toolError } from "../logging/compactTool.js";

const LayerDef = z.object({
  function: z.string().describe("Layer function: Structure, Substrate, Finish1, Finish2, Membrane, Thermal/AirGap/Insulation"),
  widthMm: z.number().optional().describe("Layer width in millimeters"),
  widthFt: z.number().optional().describe("Layer width in feet (alternative to widthMm)"),
  materialId: z.number().optional().describe("Material element ID"),
  materialName: z.string().optional().describe("Material name (case-insensitive, alternative to materialId)"),
});

const SetCompoundStructureInput = z.object({
  typeId: z.number().optional().describe("Type ID of a system family type"),
  typeName: z.string().optional().describe("Type name to search (case-insensitive)"),
  category: z.string().optional().describe("Category filter when using typeName (e.g. OST_Walls)"),
  action: z.enum(["replace", "add", "remove", "modify"]).default("replace").describe("Action: replace all layers, add a layer, remove a layer, or modify a layer"),
  dryRun: z.boolean().default(true).describe("Preview changes without applying. Default: true"),
  // For replace action
  layers: z.array(LayerDef).optional().describe("Full layer definitions (for 'replace' action)"),
  // For add action
  layer: LayerDef.optional().describe("Single layer definition (for 'add' action)"),
  position: z.number().int().optional().describe("Insert position for 'add' (0=exterior, default=append)"),
  // For remove/modify actions
  layerIndex: z.number().int().optional().describe("Layer index for 'remove' or 'modify' actions"),
  // For modify action
  function: z.string().optional().describe("New layer function (for 'modify')"),
  widthMm: z.number().optional().describe("New width in mm (for 'modify')"),
  widthFt: z.number().optional().describe("New width in ft (for 'modify')"),
  materialId: z.number().optional().describe("New material ID (for 'modify')"),
  materialName: z.string().optional().describe("New material name (for 'modify')"),
});

export function registerSetCompoundStructureTool(server: McpServer): void {
  server.tool(
    "set_compound_structure",
    "Modify compound structure (layer stratigraphy) on system family types. Actions: replace (all layers), add (insert layer), remove (delete layer), modify (change layer properties). Supports walls, floors, roofs, ceilings.",
    SetCompoundStructureInput.shape,
    async (args) => {
      const start = Date.now();
      try {
        const result = await withRevitConnection(async (client) => {
          return await client.sendCommand("set_compound_structure", args);
        });
        return toolResponse("set_compound_structure", result, Date.now() - start, args);
      } catch (error) {
        return toolError("set_compound_structure", error, Date.now() - start);
      }
    }
  );
}
