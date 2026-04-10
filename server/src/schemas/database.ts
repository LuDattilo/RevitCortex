import { z } from "zod";

export const StoreProjectDataInput = z.object({
  project_name: z.string().describe("The name of the Revit project"),
  project_path: z.string().optional().describe("File path to the project"),
  project_number: z.string().optional().describe("Project number or identifier"),
  project_address: z.string().optional().describe("Project address or location"),
  client_name: z.string().optional().describe("Client name"),
  project_status: z.string().optional().describe("Project status (e.g., Active, Completed, On Hold)"),
  author: z.string().optional().describe("Project author or creator"),
  metadata: z.record(z.string()).optional().describe("Additional project metadata as key-value pairs"),
});

const RoomSchema = z.object({
  room_id: z.string().describe("Unique identifier for the room (Revit Element ID)"),
  room_name: z.string().optional().describe("Room name"),
  room_number: z.string().optional().describe("Room number"),
  department: z.string().optional().describe("Department"),
  level: z.string().optional().describe("Level or floor"),
  area: z.number().optional().describe("Room area"),
  perimeter: z.number().optional().describe("Room perimeter"),
  occupancy: z.string().optional().describe("Occupancy type"),
  comments: z.string().optional().describe("Additional comments"),
  metadata: z.record(z.string()).optional().describe("Additional room metadata as key-value pairs"),
});

export const StoreRoomDataInput = z.object({
  project_name: z.string().describe("The name of the Revit project this room belongs to"),
  rooms: z.array(RoomSchema).min(1).describe("Array of room data to store"),
});

export const QueryStoredDataInput = z.object({
  query_type: z.enum([
    "all_projects",
    "project_by_id",
    "project_by_name",
    "rooms_by_project_id",
    "rooms_by_project_name",
    "all_rooms",
    "stats",
  ]).describe("Type of query to perform"),
  project_id: z.number().optional().describe("Project ID (required for project_by_id and rooms_by_project_id)"),
  project_name: z.string().optional().describe("Project name (required for project_by_name and rooms_by_project_name)"),
});
