#!/usr/bin/env python3
"""Generate RevitCortex User Guide PDF - English version."""

import os, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

from fpdf import FPDF

# -- Colors --
C_PRIMARY = (25, 60, 120)
C_ACCENT = (0, 130, 80)
C_DARK = (40, 40, 40)
C_GRAY = (100, 100, 100)
C_LIGHT_GRAY = (180, 180, 180)
C_BG_CODE = (242, 242, 245)
C_BG_PROMPT = (232, 245, 235)
C_BG_RESULT = (235, 242, 252)
C_BG_TIP = (255, 248, 230)
C_BG_WARN = (255, 238, 238)


class GuidePDF(FPDF):
    def header(self):
        if self.page_no() > 1:
            self.set_font("Helvetica", "I", 8)
            self.set_text_color(*C_LIGHT_GRAY)
            self.cell(0, 6, "RevitCortex - User Guide", align="L")
            self.cell(0, 6, f"Page {self.page_no()}", align="R", new_x="LMARGIN", new_y="NEXT")
            self.set_draw_color(*C_LIGHT_GRAY)
            self.line(10, 14, 200, 14)
            self.ln(4)

    def footer(self):
        self.set_y(-12)
        self.set_font("Helvetica", "I", 7)
        self.set_text_color(*C_LIGHT_GRAY)
        self.cell(0, 8, "RevitCortex v1.0 - AI Assistant for Autodesk Revit", align="C")

    def section_title(self, num, title):
        self.add_page()
        self.ln(8)
        self.set_font("Helvetica", "B", 28)
        self.set_text_color(*C_PRIMARY)
        self.cell(0, 14, f"{num}", new_x="LMARGIN", new_y="NEXT")
        self.set_font("Helvetica", "", 18)
        self.cell(0, 10, title, new_x="LMARGIN", new_y="NEXT")
        self.set_draw_color(*C_PRIMARY)
        self.set_line_width(0.5)
        self.line(10, self.get_y() + 2, 200, self.get_y() + 2)
        self.set_line_width(0.2)
        self.ln(8)

    def h2(self, title):
        if self.get_y() > 260:
            self.add_page()
        self.ln(3)
        self.set_font("Helvetica", "B", 13)
        self.set_text_color(*C_PRIMARY)
        self.cell(0, 9, title, new_x="LMARGIN", new_y="NEXT")
        self.ln(1)

    def h3(self, title):
        self.set_font("Helvetica", "B", 10.5)
        self.set_text_color(*C_DARK)
        self.cell(0, 7, title, new_x="LMARGIN", new_y="NEXT")

    def para(self, t):
        self.set_font("Helvetica", "", 9.5)
        self.set_text_color(*C_DARK)
        self.multi_cell(0, 5, t)
        self.ln(2)

    def text_small(self, t):
        self.set_font("Helvetica", "", 8)
        self.set_text_color(*C_GRAY)
        self.multi_cell(0, 4.5, t)
        self.ln(1)

    def code(self, t):
        self.set_font("Courier", "", 8)
        self.set_fill_color(*C_BG_CODE)
        self.set_text_color(50, 50, 50)
        w = self.w - self.l_margin - self.r_margin
        self.multi_cell(w, 4.5, t, fill=True)
        self.ln(2)

    def prompt(self, label, t):
        if self.get_y() > 268:
            self.add_page()
        self.set_font("Helvetica", "B", 7.5)
        self.set_text_color(*C_ACCENT)
        self.cell(0, 4, label, new_x="LMARGIN", new_y="NEXT")
        self.set_font("Helvetica", "I", 9.5)
        self.set_text_color(0, 90, 50)
        self.set_fill_color(*C_BG_PROMPT)
        w = self.w - self.l_margin - self.r_margin
        self.multi_cell(w, 5.5, f'  "{t}"', fill=True)
        self.ln(1)

    def result(self, t):
        self.set_font("Helvetica", "", 8.5)
        self.set_text_color(30, 60, 120)
        self.set_fill_color(*C_BG_RESULT)
        w = self.w - self.l_margin - self.r_margin
        self.multi_cell(w, 4.8, f"  {t}", fill=True)
        self.ln(1)

    def tip(self, t):
        self.set_font("Helvetica", "B", 8)
        self.set_text_color(160, 120, 0)
        self.set_fill_color(*C_BG_TIP)
        w = self.w - self.l_margin - self.r_margin
        self.multi_cell(w, 4.8, f"  TIP: {t}", fill=True)
        self.ln(2)

    def warn(self, t):
        self.set_font("Helvetica", "B", 8)
        self.set_text_color(180, 40, 40)
        self.set_fill_color(*C_BG_WARN)
        w = self.w - self.l_margin - self.r_margin
        self.multi_cell(w, 4.8, f"  WARNING: {t}", fill=True)
        self.ln(2)

    def tool_card(self, name, what, when, prompts_results, tips=None, warns=None):
        needed = 50 + len(prompts_results) * 20
        if self.get_y() + min(needed, 80) > 270:
            self.add_page()
        self.set_draw_color(*C_PRIMARY)
        self.set_line_width(0.6)
        y_start = self.get_y()
        self.line(10, y_start, 10, y_start + 4)
        self.set_line_width(0.2)
        self.set_font("Courier", "B", 11)
        self.set_text_color(*C_PRIMARY)
        self.cell(0, 6, f"  {name}", new_x="LMARGIN", new_y="NEXT")
        self.set_font("Helvetica", "", 9)
        self.set_text_color(*C_DARK)
        self.multi_cell(0, 5, what)
        self.ln(1)
        if when:
            self.set_font("Helvetica", "B", 8)
            self.set_text_color(*C_GRAY)
            self.cell(0, 4, "When to use:", new_x="LMARGIN", new_y="NEXT")
            self.set_font("Helvetica", "I", 8.5)
            self.multi_cell(0, 4.5, when)
            self.ln(1)
        for i, (prompt_text, result_text) in enumerate(prompts_results):
            lbl = f"Example {i+1}:" if len(prompts_results) > 1 else "Example:"
            self.prompt(lbl, prompt_text)
            if result_text:
                self.result(result_text)
        if tips:
            self.tip(tips)
        if warns:
            self.warn(warns)
        self.ln(1)
        self.set_draw_color(220, 220, 220)
        self.line(10, self.get_y(), 200, self.get_y())
        self.ln(3)


def build_pdf():
    pdf = GuidePDF("P", "mm", "A4")
    pdf.set_auto_page_break(auto=True, margin=18)

    # =======================================================
    # COVER
    # =======================================================
    pdf.add_page()
    pdf.ln(50)
    pdf.set_font("Helvetica", "B", 40)
    pdf.set_text_color(*C_PRIMARY)
    pdf.cell(0, 18, "RevitCortex", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(4)
    pdf.set_font("Helvetica", "", 20)
    pdf.set_text_color(*C_DARK)
    pdf.cell(0, 12, "Complete User Guide", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(8)
    pdf.set_draw_color(*C_PRIMARY)
    pdf.set_line_width(0.8)
    pdf.line(60, pdf.get_y(), 150, pdf.get_y())
    pdf.set_line_width(0.2)
    pdf.ln(8)
    pdf.set_font("Helvetica", "", 12)
    pdf.set_text_color(*C_GRAY)
    pdf.cell(0, 7, "AI Assistant for Autodesk Revit", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 7, "149 Tools | Revit 2023-2027", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 7, "Multilingual support: EN, IT, FR, DE", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(30)
    pdf.set_font("Helvetica", "I", 10)
    pdf.cell(0, 6, "April 2026 - v1.0", align="C", new_x="LMARGIN", new_y="NEXT")

    # =======================================================
    # TABLE OF CONTENTS
    # =======================================================
    pdf.section_title("", "Table of Contents")
    toc = [
        ("01", "Getting Started", "Prerequisites, installation, first connection"),
        ("02", "How to Talk to Revit", "Prompt strategies, roles, templates"),
        ("03", "Querying the Model", "Find elements, filter, export data"),
        ("04", "Creating Elements", "Place doors, walls, columns, grids, levels"),
        ("05", "Modifying Elements", "Change parameters, move, copy, delete"),
        ("06", "Types and Families", "Duplicate types, change type, manage families"),
        ("07", "Views and Sheets", "Create plans, sections, 3D views, sheets, viewports"),
        ("08", "Schedules", "Create schedules, export, modify fields"),
        ("09", "Annotations and Tags", "Tag rooms, dimension, text notes, legends"),
        ("10", "Materials and Compound Structures", "Manage materials, compound layers"),
        ("11", "Linked Files", "Navigate links, move, reload, highlight"),
        ("12", "Rename and Numbering", "Bulk rename, number rooms/doors"),
        ("13", "Advanced Parameters", "Shared parameters, project parameters, transfer"),
        ("14", "Analysis and Quality", "Health check, warnings, clash, family audit"),
        ("15", "Model Cleanup", "Purge, CAD cleanup, empty tags"),
        ("16", "Composite Workflows", "Full audit, data roundtrip, sheet set"),
        ("17", "Security", "Sandbox, read-only, audit log, confirmations"),
        ("18", "Session Optimization", "Session patterns, token management"),
        ("19", "IFC", "Import, export, native reconstruction"),
        ("", "Appendix", "Ready-to-use prompt library by scenario"),
    ]
    for num, title, desc in toc:
        pdf.set_font("Helvetica", "B", 10)
        pdf.set_text_color(*C_PRIMARY)
        pdf.cell(12, 6, num)
        pdf.set_text_color(*C_DARK)
        pdf.cell(55, 6, title)
        pdf.set_font("Helvetica", "", 9)
        pdf.set_text_color(*C_GRAY)
        pdf.cell(0, 6, desc, new_x="LMARGIN", new_y="NEXT")

    # =======================================================
    # 01 - GETTING STARTED
    # =======================================================
    pdf.section_title("01", "Getting Started")
    pdf.para("RevitCortex is an AI assistant that lets you control Autodesk Revit using natural language. "
             "Instead of navigating menus and dialogs, you talk to Claude and it executes operations in the model for you.")
    pdf.para("You can ask things like:")
    pdf.prompt("", "How many walls are in the model and what types are used?")
    pdf.prompt("", "Set the Manufacturer parameter to 'ACME' for all doors on Level 1")
    pdf.prompt("", "Create a room schedule with name, number, area and department")

    pdf.h2("What you need")
    pdf.para("1. Autodesk Revit 2023, 2024, 2025, 2026 or 2027\n"
             "2. Claude Desktop with Pro or Max plan (download from claude.ai/download)\n"
             "   or Claude Code (CLI)\n"
             "3. The RevitCortex package (ZIP with installer included)")

    pdf.h2("Installation")
    pdf.para("Extract the RevitCortex ZIP to a folder of your choice, then double-click install.bat "
             "(or right-click install.ps1 > Run with PowerShell). "
             "The installer requires administrator privileges and in a few seconds will:")
    pdf.para("  - Copy the plugin to the Addins folders for all detected Revit versions\n"
             "  - Install the MCP server to %USERPROFILE%\\.revitcortex\\server\\\n"
             "  - Automatically configure Claude Desktop or Claude Code as you choose")
    pdf.para("No Node.js, Python or other runtime required: the server is a self-contained executable (.exe).")
    pdf.para("After installation, the Claude Desktop configuration is already set up:")
    pdf.code('{\n  "mcpServers": {\n    "revitcortex": {\n'
             '      "command": "C:\\\\Users\\\\<name>\\\\' + '.revitcortex\\\\server\\\\RevitCortex.Server.exe",\n'
             '      "args": []\n    }\n  }\n}')

    pdf.h2("Your first session")
    pdf.para("Follow these steps in order:")
    pdf.para("1. Open your Revit project")
    pdf.para("2. Click 'Cortex Switch' in the Revit ribbon to start the server")
    pdf.para("3. Restart Claude Desktop")
    pdf.para("4. Look for the hammer icon in Claude's input bar")
    pdf.para("5. Test the connection:")
    pdf.prompt("Your first message:", "Hello Revit, are you connected?")
    pdf.result("Claude will call say_hello and respond with a confirmation message from the plugin.")
    pdf.para("6. Orient yourself in the model:")
    pdf.prompt("Second message:", "What is this project? Show me levels, links and worksets")
    pdf.result("You will receive a summary: project name, author, levels with elevations, links, active worksets.")
    pdf.tip("The first get_project_info call of the session should be complete. Subsequent calls can filter for efficiency.")

    # =======================================================
    # 02 - HOW TO TALK TO REVIT
    # =======================================================
    pdf.section_title("02", "How to Talk to Revit")
    pdf.para("You don't need to learn command names or special syntax. Talk as you would to an expert BIM colleague. "
             "Claude understands context and automatically selects the right tool.")

    pdf.h2("Set a role at the start")
    pdf.para("Begin each session by telling Claude who it is:")
    pdf.prompt("Recommended role:", "You are a BIM expert working in Revit. Only use the available MCP tools. "
               "Ask for confirmation before making significant changes.")
    pdf.result("Claude will be more precise in tool selection and will ask for confirmation on destructive operations.")

    pdf.h2("Be specific")
    pdf.para("The more detail you provide, the better the results:")
    pdf.prompt("Vague (not recommended):", "Show me the walls")
    pdf.prompt("Specific (recommended):", "Show me all walls of type 'Basic Wall: Concrete 200mm' on Level 1 with fire resistance rating")
    pdf.tip("Always specify: category + type + level + parameters of interest.")

    pdf.h2("Preview before modifying")
    pdf.para("For bulk operations, always ask for a preview first:")
    pdf.prompt("Step 1 - Preview:", "Which doors would be modified if I change the Mark for all doors on Level 2?")
    pdf.result("Claude will run a simulation (dryRun) showing how many elements would be affected.")
    pdf.prompt("Step 2 - Confirm:", "OK, proceed with the change")
    pdf.result("Revit will show a native confirmation dialog before applying the changes.")

    pdf.h2("Useful prompt templates")
    templates = [
        ("Query", "List all [CATEGORY] on [LEVEL] where [PARAMETER] = [VALUE]"),
        ("Count", "Count [CATEGORY] grouped by [PARAMETER] and show as a table"),
        ("Check", "Find all [CATEGORY] where [PARAMETER] is empty"),
        ("Modify", "Set [PARAMETER] = [VALUE] for all [CATEGORY] of type [TYPE] - show me first"),
        ("Rename", "Rename all [TYPE] views using the format [FORMAT]"),
        ("Export", "Export data for all [CATEGORY] with [PARAMETERS] as CSV"),
        ("Duplicate type", "Duplicate type [TYPE] as [NEW_NAME] and set [PARAMETER] to [VALUE]"),
        ("Create sheets", "Create sheets for all [TYPE] views, number them [FORMAT]"),
        ("Compare", "Compare values of [PARAMETER] between Level 1 and Level 2 for [CATEGORY]"),
    ]
    for label, template in templates:
        pdf.set_font("Helvetica", "B", 9)
        pdf.set_text_color(*C_ACCENT)
        pdf.cell(28, 5.5, label)
        pdf.set_font("Helvetica", "", 9)
        pdf.set_text_color(*C_DARK)
        pdf.cell(0, 5.5, template, new_x="LMARGIN", new_y="NEXT")

    # =======================================================
    # 03 - QUERYING THE MODEL
    # =======================================================
    pdf.section_title("03", "Querying the Model")
    pdf.para("These tools read data from the model without modifying it. They are safe to use at any time.")

    pdf.tool_card("get_project_info",
        "Gets general project information: name, author, levels, worksets, phases, linked files.",
        "At the start of every session to understand the model you are working with.",
        [("What is this project? Show me all the details",
          "Name: Residential Building A | Author: Studio XYZ | 5 levels | 3 worksets | 2 links"),
         ("Show me only the levels with their elevations",
          "Ground Floor: 0.00m | Level 1: 3.50m | Level 2: 7.00m | Roof: 10.50m")],
        "The first call should include everything. Subsequent calls can filter to save tokens.")

    pdf.tool_card("get_element_parameters",
        "Shows all parameters (instance and type) of one or more elements, given their ID.",
        "When you want to inspect a specific element or understand what parameters it has.",
        [("Show me all parameters of element 606873",
          "Full list: Type Name, Family, Level, Height, Width, Fire Rating, Comments..."),
         ("What are the type parameters of this door?",
          "Type params: [Type] Width = 900mm | [Type] Height = 2100mm | [Type] Fire Rating = EI60")])

    pdf.tool_card("ai_element_filter",
        "Intelligent filter to find elements by category. Supports filters on types, instances, and limits.",
        "When you want a quick list of elements from a specific category.",
        [("Find all structural columns in the model",
          "Found 47 structural columns: 12 HEB240, 20 HEB300, 15 HEB360..."),
         ("Show me the first 5 wall types",
          "5 types: Basic Wall 200mm | Basic Wall 300mm | Curtain Wall | Partition 100mm | Fire Wall REI120")],
        "Always wrap parameters in the 'data' object: {\"data\": {\"filterCategory\": \"OST_Walls\"}}")

    pdf.tool_card("filter_by_parameter_value",
        "Search elements by filtering on a specific parameter value. Supports: equals, contains, greater than, empty, etc.",
        "When you need elements matching precise conditions on a parameter.",
        [("Find all doors where fire rating is empty",
          "18 doors with no fire rating: D-101, D-105, D-112... (IDs: 12345, 12389, 12456)"),
         ("Which walls on Level 1 are wider than 200mm?",
          "32 walls found, all of type 'Basic Wall 300mm' or 'Fire Wall REI120'"),
         ("Find rooms with no number assigned",
          "5 rooms with no number: Room 1 (ID: 789), Room 2 (ID: 801)...")],
        "For type parameters, always use parameterType: 'type'. The default 'both' may not resolve correctly.")

    pdf.tool_card("get_current_view_elements",
        "Lists elements visible in the active view. You can filter by category and limit the count.",
        "When you want to analyze only what is visible in the current view.",
        [("How many doors are in this view?",
          "42 doors visible in view 'Ground Floor - Architectural'"),
         ("List all visible walls with type and length",
          "Table: ID | Type | Length | 68 walls total, total length 1,247m")])

    pdf.tool_card("get_selected_elements",
        "Reads elements currently selected in Revit.",
        "When you have selected elements in Revit and want to work with them from Claude.",
        [("What do I have selected?",
          "3 elements selected: Wall 12345 (Basic Wall 200mm), Door 12389 (M_Single-Flush 900x2100), Window 12456"),
         ("Show me the parameters of the selected elements",
          "Full detail for each element with all instance and type parameters")])

    pdf.tool_card("export_elements_data",
        "Exports element data as a JSON or CSV table. Choose categories, parameters, and apply filters.",
        "When you need to create a report or export data for external analysis.",
        [("Export all door data as CSV with type, level and fire rating",
          "CSV generated with 156 rows: ID, Type, Level, Fire Rating"),
         ("Give me a table of walls with type name and length",
          "Formatted table: Type | Count | Total Length | 12 types, 347 walls")],
        "Always specify parameterNames and maxElements to limit the response.")

    pdf.tool_card("export_room_data",
        "Exports all room data: name, number, area, finishes, department.",
        "For space reports, area verification, or export for the project brief.",
        [("Export all room data with areas and departments",
          "85 rooms exported: Office 101 (25.3 sqm, Dept A) | Corridor 102 (12.1 sqm)..."),
         ("Are there any unplaced or unbounded rooms?",
          "3 unplaced rooms, 2 unbounded rooms (area = 0)")])

    pdf.tool_card("export_to_excel",
        "Exports element data directly to an Excel file (.xlsx).",
        "When you need an Excel file to share with the team or consultants.",
        [("Export all doors to an Excel file on the desktop",
          "File saved: C:/Users/.../Desktop/Doors.xlsx - 156 rows, 12 columns"),
         ("Create an Excel file with room data for the client",
          "File generated with sheet 'Rooms': Name, Number, Area, Department, Floor Finish")])

    pdf.tool_card("get_linked_elements",
        "Query elements inside linked Revit files.",
        "When you need to check data in linked models without opening them.",
        [("Show all walls from the structural link",
          "234 walls found in link 'Structure.rvt': 120 CLS300, 89 CLS200..."),
         ("How many doors are in the linked architectural model?",
          "312 doors in link 'Architecture.rvt', distributed across 5 levels")])

    pdf.tool_card("get_elements_in_spatial_volume",
        "Find which elements are inside a room or a custom volume.",
        "For room inventories, furniture checks, or space analysis.",
        [("What furniture is in room 101?",
          "8 elements: 2 desks, 4 chairs, 1 cabinet, 1 lamp"),
         ("List all elements in the director's office",
          "15 elements found by category: Furniture (6), Equipment (4), Lighting (5)")])

    pdf.tool_card("get_room_openings",
        "Find the doors and windows associated with each room.",
        "For opening reports, egress verification, or room data sheets.",
        [("What doors and windows does room 305 have?",
          "2 doors (M_Single-Flush 900x2100, M_Double-Flush 1800x2100) and 3 windows (Fixed 1200x1500)"),
         ("Show the openings for all rooms on Level 1",
          "Table by room: Room | Doors | Windows | 25 rooms analysed")])

    pdf.tool_card("find_untagged_elements",
        "Find elements without a tag in the current view.",
        "For documentation quality control.",
        [("Which doors are not tagged in this view?",
          "12 untagged doors: D-101, D-102, D-105... (placed but not annotated)"),
         ("Find untagged rooms",
          "3 untagged rooms on Ground Floor")])

    pdf.tool_card("find_undimensioned_elements",
        "Find elements with no dimensions in the active view.",
        "To complete documentation and verify everything is dimensioned.",
        [("Which walls are not dimensioned in this floor plan?",
          "8 undimensioned walls: IDs 456, 789, 1023... in view 'Ground Floor'")])

    pdf.tool_card("measure_between_elements",
        "Measure the distance between two elements or two points.",
        "For quick distance checks without using Revit's dimension tool.",
        [("How far is column A1 from column B1?",
          "Center-to-center distance: 6.00m | Minimum distance: 5.76m"),
         ("Measure the distance between these two walls",
          "Bounding box distance: 3.45m")])

    # =======================================================
    # 04 - CREATING ELEMENTS
    # =======================================================
    pdf.section_title("04", "Creating Elements")
    pdf.para("These tools create new elements in the model. Revit will always show a confirmation dialog "
             "before executing important operations.")

    pdf.tool_card("create_point_based_element",
        "Place point-based elements: doors, windows, columns, furniture, equipment.",
        "When you need to place one or more elements at specific positions.",
        [("Place a desk at coordinates (5, 3, 0) on Level 1",
          "Desk created: ID 67890, family 'Desk 1500x800', position (5.0, 3.0, 0.0)"),
         ("Place a column at every grid intersection",
          "12 columns created at intersections A1-A4, B1-B4, C1-C4")])

    pdf.tool_card("create_line_based_element",
        "Create line-based elements: walls, beams, pipes, cables.",
        "To create walls, beams or other linear elements defined by start and end points.",
        [("Create a wall from point (0,0) to point (10,0) at Ground Floor",
          "Wall created: ID 67891, type 'Basic Wall 200mm', length 10.0m"),
         ("Add an HEB300 beam from (0,0,3.5) to (8,0,3.5)",
          "Beam created: ID 67892, type HEB300, span 8.0m")])

    pdf.tool_card("create_floor",
        "Create a floor from a boundary of points or from a room perimeter.",
        "To add floors to existing rooms or create custom slabs.",
        [("Create a concrete floor in room 205",
          "Floor created: ID 67893, type 'Concrete 150mm', area 25.3 sqm"),
         ("Add a slab at Level 2 with these 4 corner points",
          "Slab created with custom boundary, area 120 sqm")])

    pdf.tool_card("create_room",
        "Create a room element at a specific position.",
        "When rooms are missing or you need to add new ones.",
        [("Create a room 'Office' on Level 1 at coordinates (5, 8)",
          "Room created: 'Office', auto-assigned number, area 18.5 sqm"),
         ("Add room 'Archive' with number 201 on Level 2",
          "Room 201 'Archive' created on Level 2")])

    pdf.tool_card("create_grid",
        "Create a grid line system with regular spacing.",
        "At the start of a project or when adding a structural grid.",
        [("Create a 5x4 grid with 6m spacing, axes A-E and 1-4",
          "Grid created: 5 X-axes (A-E) + 4 Y-axes (1-4), 6.0m spacing"),
         ("Add 3 horizontal grids with 8m spacing starting from 1",
          "3 grids created: 1, 2, 3 with 8.0m spacing")])

    pdf.tool_card("create_level",
        "Create a new level at a specific elevation.",
        "To add floors, mezzanines, or roof levels.",
        [("Create Level 3 at elevation 10.5 metres",
          "Level 'Level 3' created at +10.500m, floor plan created automatically"),
         ("Add a mezzanine at elevation 4.5m without creating floor plans",
          "Level 'Mezzanine' created at +4.500m, no associated views")])

    pdf.tool_card("create_array",
        "Create linear or radial arrays of existing elements.",
        "To replicate elements with regular spacing.",
        [("Copy this column 5 times with 3m spacing in the X direction",
          "5 copies created: 3.0m spacing, IDs: 67901-67905"),
         ("Create a radial array of 8 elements around centre (5,5)",
          "8 copies created at 45-degree intervals, radius 3.0m")])

    pdf.tool_card("create_structural_framing_system",
        "Create a beam grid on a specified level.",
        "To quickly place a regular beam system.",
        [("Create a beam system on Level 2 with 1.2m spacing between axes A-D",
          "12 beams created: type IPE240, span 6.0m, spacing 1.2m")])

    pdf.tool_card("copy_elements",
        "Copy elements with an X, Y, Z offset.",
        "To duplicate groups of elements with a displacement.",
        [("Copy these elements 3 metres to the right",
          "5 elements copied with offset X=3.0m, new IDs: 68001-68005"),
         ("Duplicate this group to the floor above",
          "Elements copied with offset Z=3.5m")])

    pdf.tool_card("create_filled_region",
        "Create a filled region in a view.",
        "To highlight zones in plans or create graphic areas.",
        [("Draw a filled region around this zone",
          "Region created with fill pattern 'Solid Fill', area 45 sqm")])

    pdf.tool_card("create_surface_based_element",
        "Create surface-based elements such as roofs, floors or ceilings by defining perimeter points.",
        "To create elements with custom geometry from coordinates.",
        [("Create a roof following these perimeter points",
          "Roof created: type 'Generic - 400mm', area 125 sqm, 4 slopes"),
         ("Create a custom-shaped floor in this zone",
          "Floor created: type 'Concrete Slab 200mm', area 68 sqm")])

    # =======================================================
    # 05 - MODIFYING ELEMENTS
    # =======================================================
    pdf.section_title("05", "Modifying Elements")
    pdf.para("All modifications show a confirmation dialog in Revit. You can always undo with Ctrl+Z.")
    pdf.warn("Bulk modifications are powerful. Always use dryRun or ask for a preview before applying.")

    pdf.tool_card("set_element_parameters",
        "Set the value of one or more parameters on one or more elements.",
        "To modify any writable parameter: comments, mark, phase, etc.",
        [("Set the Mark of door 12345 to 'D-001'",
          "Parameter updated: door 12345, Mark = 'D-001'"),
         ("Change Comments to 'Verified' for elements 100, 200, 300",
          "3 elements updated: Comments = 'Verified'"),
         ("Set fire rating 'EI60' for all selected doors",
          "Revit confirmation -> 8 doors updated")])

    pdf.tool_card("modify_element",
        "Move, rotate, mirror or copy elements in the model.",
        "To reposition elements without selecting them manually in Revit.",
        [("Move wall 5678 by 2 metres in the X direction",
          "Wall moved: translation X=2.0m, new position confirmed"),
         ("Rotate this element 45 degrees around its centre",
          "Rotation applied: 45 degrees counter-clockwise"),
         ("Mirror these 3 elements about the Y axis",
          "3 elements mirrored successfully")])

    pdf.tool_card("change_element_type",
        "Change the type of one or more elements.",
        "When you need to update elements to a different type of the same family.",
        [("Change all 'Basic Wall 200mm' walls to 'Basic Wall 300mm'",
          "Revit confirmation -> 45 walls updated to new type"),
         ("Replace these doors with type 'Fire Door EI60'",
          "12 doors changed to type 'Fire Door EI60'")])

    pdf.tool_card("operate_element",
        "Select elements in Revit, or apply quick graphic overrides.",
        "To highlight problematic elements or select them for further work.",
        [("Select elements 123, 456 and 789 in Revit",
          "3 elements selected in the active view"),
         ("Highlight the problematic walls in red",
          "Graphic override applied: red colour, 15 elements")])

    pdf.tool_card("color_elements",
        "Colour elements by category based on a parameter value.",
        "For thematic visualisation: rooms by department, walls by type, etc.",
        [("Colour all walls by type",
          "View coloured: 5 colours assigned to 5 types, legend created"),
         ("Show rooms coloured by department",
          "Rooms coloured: Marketing=blue, IT=green, HR=orange...")])

    pdf.tool_card("match_element_properties",
        "Copy parameter values from a source element to one or more target elements.",
        "To apply the same properties to similar elements without retyping.",
        [("Copy the Mark and Comments from element A to elements B, C and D",
          "2 parameters copied to 3 elements: Mark, Comments"),
         ("Apply door 123's properties to all other doors of this type",
          "8 parameters transferred to 24 doors")])

    pdf.tool_card("delete_element",
        "Delete elements from the model by ID.",
        "When you need to remove specific elements.",
        [("Delete elements 123, 456 and 789",
          "Revit confirmation: 'Delete 3 elements?' -> 3 elements deleted"),
         ("Remove unused grids",
          "dryRun: 4 grids would be deleted. Proceed? -> 4 grids removed")],
        warns="Irreversible operation (beyond Ctrl+Z). Always use dryRun first.")

    pdf.tool_card("save_selection",
        "Save the current selection as a named set.",
        "To save a group of selected elements for later reuse.",
        [("Save this selection as 'Walls to check'",
          "Selection 'Walls to check' saved with 18 elements")])

    pdf.tool_card("load_selection",
        "Load a previously saved selection and select it in the view.",
        "To retrieve a saved element set and continue working with it.",
        [("Load selection 'Walls to check'",
          "18 elements selected from selection 'Walls to check'")])

    pdf.tool_card("delete_selection",
        "Delete a saved selection set.",
        "To clean up selection sets no longer needed.",
        [("Delete saved selection 'Walls to check'",
          "Selection set 'Walls to check' deleted")])

    pdf.tool_card("set_element_phase",
        "Set the creation or demolition phase of elements.",
        "To manage project phases (existing, new construction, demolition).",
        [("Set phase 'New Construction' for these walls",
          "12 walls updated: Phase Created = 'New Construction'"),
         ("Mark these elements as demolished in phase 2",
          "8 elements set as demolished")])

    pdf.tool_card("set_element_workset",
        "Move elements to a different workset.",
        "To organise elements into the correct worksets in workshared models.",
        [("Move all doors to workset 'Architecture - Doors'",
          "156 doors moved to workset 'Architecture - Doors'"),
         ("Change the workset of selected elements",
          "23 elements moved to the specified workset")])

    pdf.tool_card("override_graphics",
        "Apply graphic overrides (colour, transparency, line weight) to specific elements.",
        "To highlight elements in a view without changing their properties.",
        [("Colour these walls red with 50% transparency",
          "Override applied: red (255,0,0), 50% transparency, 8 elements"),
         ("Reset graphic overrides for element 123",
          "Overrides removed for element 123")])

    pdf.tool_card("send_code_to_revit",
        "Execute custom C# code inside Revit. Code is validated by a security sandbox.",
        "For advanced operations not covered by the other tools.",
        [("Run this code to rename all sheets",
          "Code executed successfully. 12 sheets renamed.")],
        warns="Code cannot access the filesystem, network, registry or processes. Forbidden namespaces: System.IO, System.Net, System.Diagnostics.Process.")

    # =======================================================
    # 06 - TYPES AND FAMILIES
    # =======================================================
    pdf.section_title("06", "Types and Families")
    pdf.para("Manage family types: duplicate to create variants, load new families, list available types.")

    pdf.tool_card("duplicate_family_type",
        "Duplicate a loadable family type (door, window, furniture, etc.) with a new name. "
        "You can also modify the new type's parameters in the same operation.",
        "When you want to create a variant of an existing type - e.g. a narrower door or taller window.",
        [("Duplicate door '900x2100' as '800x2100' and set width to 800",
          "Type created: '800x2100' in family M_Single-Flush, Width=800mm set"),
         ("Create a variant of window 'Fixed 1200x1500' called 'Fixed 1500x1500' with height 1500",
          "Type duplicated: 'Fixed 1500x1500', parameter Height=1500mm applied")],
        "If the same type name exists in multiple families, specify familyName to disambiguate.")

    pdf.tool_card("duplicate_system_type",
        "Duplicate a system family type (wall, floor, roof, ceiling).",
        "To create variants of system types without starting from scratch.",
        [("Duplicate wall type 'Basic Wall 200mm' as 'Basic Wall 250mm'",
          "Type created: 'Basic Wall 250mm', category Walls"),
         ("Create a copy of floor type 'Concrete 150mm'",
          "Type 'Concrete 150mm - Copy' created")])

    pdf.tool_card("load_family",
        "Load a .rfa family into the model, or list available families by category.",
        "To import families from external files or explore the model catalogue.",
        [("Load the family from file C:/Families/CustomDoor.rfa",
          "Family 'CustomDoor' loaded with 3 types"),
         ("List available families in the Doors category",
          "8 families: M_Single-Flush (4 types), M_Double-Flush (2 types)...")])

    pdf.tool_card("get_available_family_types",
        "List all available family types, with filter by category or family name.",
        "To know which types you can use before creating or changing elements.",
        [("Show all available door types",
          "24 types in 5 families: M_Single-Flush (900x2100, 800x2100, 700x2100)..."),
         ("What types does the 'Fixed Window' family have?",
          "3 types: 1200x1500, 1500x1500, 900x1200")])

    pdf.tool_card("export_families",
        "Export families from the model as .rfa files to disk.",
        "To extract families for reuse in other projects.",
        [("Export all door families to folder C:/Export",
          "12 families exported to C:/Export/Doors/: M_Single-Flush.rfa, M_Double-Flush.rfa..."),
         ("Export furniture families grouped by category",
          "8 families exported in subfolders by category")])

    # =======================================================
    # 07 - VIEWS AND SHEETS
    # =======================================================
    pdf.section_title("07", "Views and Sheets")

    pdf.tool_card("create_view",
        "Create floor plans, reflected ceiling plans, sections, or 3D views.",
        "When you need a new view of the model.",
        [("Create a floor plan of Level 2 at scale 1:100",
          "View 'Level 2' created, scale 1:100, ID: 78901"),
         ("Create a section looking north",
          "Section created: north direction, depth 20m"),
         ("Create a 3D view of the model",
          "3D view created with auto-generated name")])

    pdf.tool_card("duplicate_view",
        "Duplicate views with options: simple copy, with detailing, or as dependent.",
        "To create working copies of existing views.",
        [("Duplicate this view with all detailing",
          "View duplicated with details: 'Ground Floor - Copy'"),
         ("Create 3 dependent views from Level 1 plan",
          "3 dependent views created")])

    pdf.tool_card("create_view_filter",
        "Create view filters and apply them with graphic overrides.",
        "To highlight or hide elements based on rules.",
        [("Create a filter that shows fire-rated walls in red",
          "Filter 'Fire Walls' created, applied to view with red colour"),
         ("Hide demolished elements in the current view",
          "Filter 'Demolished' applied, elements hidden")])

    pdf.tool_card("apply_view_template",
        "Apply or remove view templates.",
        "To standardise the appearance of views.",
        [("Apply the 'Structural Plan' template to all floor plans",
          "Template applied to 8 views"),
         ("Remove the template from selected views",
          "Template removed from 3 views")])

    pdf.tool_card("rename_views",
        "Rename views in bulk with prefixes, suffixes, or find-and-replace.",
        "To standardise view names.",
        [("Add prefix 'REV-' to all sections",
          "12 sections renamed: 'Section 1' -> 'REV-Section 1'..."),
         ("Replace 'OLD' with 'NEW' in view names",
          "5 views renamed")])

    pdf.tool_card("create_sheet",
        "Create a single sheet with a title block.",
        "To add sheets to the drawing set.",
        [("Create sheet A-101 'Ground Floor Plan'",
          "Sheet A-101 created with standard title block")])

    pdf.tool_card("batch_create_sheets",
        "Create multiple sheets at once.",
        "To quickly set up a project's sheet set.",
        [("Create sheets from A-101 to A-110 with our company title block",
          "10 sheets created: A-101...A-110 with 'Company Titleblock'")])

    pdf.tool_card("place_viewport",
        "Place a view on a sheet.",
        "To compose sheets with the desired views.",
        [("Place Level 1 plan on sheet A-101",
          "Viewport placed at centre of sheet A-101")])

    pdf.tool_card("align_viewports",
        "Align viewports across different sheets.",
        "To maintain graphic consistency between sheets.",
        [("Align all viewports to the first one's position",
          "5 viewports aligned to the reference position")])

    pdf.tool_card("duplicate_sheet_with_content",
        "Duplicate a sheet with all its content (views, legends, schedules).",
        "To create complete sheet copies.",
        [("Make 3 copies of sheet A-101 with all views",
          "3 sheets duplicated: A-101a, A-101b, A-101c with copied views")])

    pdf.tool_card("duplicate_sheet_with_views",
        "Duplicate sheet with advanced options for views (duplicate, with detailing, dependent).",
        "When you want to control how views are duplicated in the copy.",
        [("Duplicate the sheet with dependent views",
          "Sheet duplicated, 3 views created as dependents")])

    pdf.tool_card("create_views_from_rooms",
        "Automatically create views (callouts, sections, elevations) centred on rooms.",
        "For automatic documentation of interior spaces.",
        [("Create sections for all rooms on Level 1",
          "25 sections created, one per room, name 'Section - Office 101'..."),
         ("Generate callouts for rooms 101-110",
          "10 callouts created in the corresponding floor plans")])

    pdf.tool_card("create_placeholder_sheets",
        "Create placeholder sheets for drawing set planning.",
        "To plan the sheet set before views are ready.",
        [("Create placeholder sheets for the drawing set",
          "15 placeholders created: S-001...S-015")])

    pdf.tool_card("manage_view_templates",
        "List, duplicate, delete or rename view templates.",
        "To manage the template library.",
        [("List all view templates",
          "12 templates: Architectural Plan, Structural Plan, Section, 3D...")])

    pdf.tool_card("manage_unplaced_views",
        "Find or delete views not placed on any sheet.",
        "For model cleanup, removing orphaned views.",
        [("Show me all views not placed on sheets",
          "34 unplaced views: 12 floor plans, 8 sections, 14 3D views"),
         ("Delete unplaced floor plans (preview first)",
          "dryRun: 12 floor plans would be deleted. Confirm?")])

    pdf.tool_card("batch_modify_view_range",
        "Modify the view range (cut plane, top, bottom) for multiple views.",
        "To standardise view range across multiple floor plans.",
        [("Set cut plane to 1200mm for all floor plans",
          "8 floor plans updated: cut plane = 1200mm")])

    pdf.tool_card("section_box_from_selection",
        "Create a 3D view with a section box around selected elements.",
        "To isolate and visualise specific elements in 3D.",
        [("Create a cropped 3D view around these beams",
          "3D view 'Section Box - Beams' created with automatic box + 0.5m offset")])

    pdf.tool_card("get_current_view_info",
        "Shows information about the active view in Revit.",
        "To know which view you are in before operating.",
        [("Which view am I in?",
          "Active view: 'Ground Floor - Architectural', type FloorPlan, scale 1:100")])

    pdf.tool_card("batch_export",
        "Export views and sheets to CAD formats (DWG, DXF, DGN) or images.",
        "To deliver sheets in CAD format or generate images for presentations.",
        [("Export all sheets to DWG",
          "24 sheets exported to DWG in the output folder"),
         ("Export Ground Floor views as PNG images",
          "3 views exported: Ground Floor - Architectural.png, Ground Floor - Structural.png...")])

    # =======================================================
    # 08 - SCHEDULES
    # =======================================================
    pdf.section_title("08", "Schedules")

    pdf.tool_card("create_preset_schedule",
        "Create a schedule from a preset template: doors by room, windows, rooms, materials, sheet list.",
        "The fastest way to create standard schedules.",
        [("Create a door schedule by room",
          "Schedule 'Doors by Room' created with fields: Room, Door Number, Type, Dimensions"),
         ("Create a sheet list",
          "Sheet list created with Number, Name, Revision")])

    pdf.tool_card("create_schedule",
        "Create a custom schedule by choosing category, type and fields.",
        "When presets are not enough and you need a tailored schedule.",
        [("Create a door schedule with Name, Mark and Fire Rating",
          "Schedule 'Doors' created with 3 fields, sorted by Name")])

    pdf.tool_card("get_schedule_data",
        "Read data from an existing schedule as a table.",
        "To consult schedule data without opening it in Revit.",
        [("Show me the data in 'Door Schedule'",
          "156 rows: Door D-001 | M_Single-Flush 900x2100 | EI60 | Level 1..."),
         ("Read the first 20 rows of the room list",
          "20 rows with Name, Number, Area, Department")])

    pdf.tool_card("export_schedule",
        "Export a schedule as a CSV/TSV file.",
        "To share data with external teams or import into Excel.",
        [("Export the door schedule as CSV",
          "File saved: DoorSchedule.csv, 156 rows exported")])

    pdf.tool_card("modify_schedule",
        "Add/remove fields, set sorting, rename a schedule.",
        "To modify the structure of an existing schedule.",
        [("Add the 'Fire Rating' field to the door schedule",
          "Field added in last position"),
         ("Sort the room schedule by Level then by Name",
          "Sorting applied: 1) Level, 2) Name")])

    pdf.tool_card("duplicate_schedule",
        "Duplicate an existing schedule with a new name.",
        "To create schedule variants without starting from scratch.",
        [("Duplicate 'Door Schedule' as 'Door Schedule - QC'",
          "Schedule duplicated: 'Door Schedule - QC' with same fields and filters")])

    pdf.tool_card("delete_schedule",
        "Delete a schedule from the model.",
        "To remove obsolete or duplicate schedules.",
        [("Delete schedule 'Old Door Schedule'",
          "Revit confirmation -> Schedule deleted")])

    pdf.tool_card("create_revision",
        "Create a revision or add revisions to sheets.",
        "To manage the project revision cycle.",
        [("Create a new revision with today's date and description 'Client Review'",
          "Revision #3 created: date 2026-04-13, 'Client Review'"),
         ("Add revision 1 to sheets A-101 through A-110",
          "Revision added to 10 sheets")])

    pdf.tool_card("list_schedulable_fields",
        "Show which fields you can add to a schedule for a given category.",
        "Before creating a schedule, to know which parameters are available.",
        [("Which fields can I use in a door schedule?",
          "42 fields available: Family and Type, Width, Height, Fire Rating, Level, Room...")])

    # =======================================================
    # 09 - ANNOTATIONS AND TAGS
    # =======================================================
    pdf.section_title("09", "Annotations and Tags")

    pdf.tool_card("tag_rooms",
        "Add tags to all rooms in the current view.",
        "To quickly annotate rooms after creating or checking them.",
        [("Tag all rooms in this view",
          "25 tags added in view 'Ground Floor'"),
         ("Add tags with leaders to rooms 101-110",
          "10 tags with leaders placed")])

    pdf.tool_card("tag_walls",
        "Add tags to walls in the current view.",
        "To annotate walls with their type in floor plans.",
        [("Tag all walls in this view",
          "68 wall tags added in the floor plan")])

    pdf.tool_card("create_dimensions",
        "Create dimension lines between elements.",
        "To dimension distances between grids, walls, or other elements.",
        [("Dimension the distance between grid A and grid B",
          "Dimension created: 6.00m between Grid A and Grid B")])

    pdf.tool_card("create_text_note",
        "Create text notes in views.",
        "To add annotations and comments in views.",
        [("Add a note 'Check on site' at this position",
          "Text note created in the active view")])

    pdf.tool_card("create_color_legend",
        "Create a colour legend for parameter values.",
        "To accompany coloured views with a readable legend.",
        [("Create a legend for rooms coloured by department",
          "Legend created with 6 colours: Marketing=blue, IT=green, HR=orange...")])

    pdf.tool_card("import_table",
        "Import a CSV/TSV table into a drafting view or legend.",
        "To insert external tables into Revit documentation.",
        [("Import the table from the CSV file into the drafting view",
          "Table imported: 25 rows x 5 columns in view 'Details'")])

    pdf.tool_card("wipe_empty_tags",
        "Remove all tags with empty values from the current view.",
        "To clean documentation from unfilled tags.",
        [("Delete empty room tags in the current view",
          "dryRun: 8 empty tags found. Proceed? -> 8 tags removed"),
         ("Clean all empty door tags",
          "12 empty door tags removed")])

    # =======================================================
    # 10 - MATERIALS AND COMPOUND STRUCTURES
    # =======================================================
    pdf.section_title("10", "Materials and Compound Structures")

    pdf.tool_card("get_materials",
        "List materials in the model, with filter by class or name.",
        "To explore the model's material library.",
        [("Show all materials in the model",
          "48 materials: Concrete (5), Steel (3), Glass (2), Brick (4)..."),
         ("Find materials with 'Concrete' in the name",
          "3 materials: Concrete C25/30, Lightweight Concrete, Reinforced Concrete")])

    pdf.tool_card("get_material_properties",
        "Show detailed properties of a material.",
        "To inspect colour, transparency, class and assets of a material.",
        [("Show properties of material 'Concrete - Cast-in-Place'",
          "Class: Concrete | Colour: grey (180,180,180) | Transparency: 0% | Structural asset: present")])

    pdf.tool_card("get_material_quantities",
        "Calculate material quantities used by category.",
        "For quantity takeoffs and material analysis.",
        [("How much material is used in walls and floors?",
          "Walls: Concrete 450 sqm, Brick 320 sqm | Floors: Ceramic 280 sqm, Concrete 150 sqm")])

    pdf.tool_card("create_material",
        "Create a new material with colour, transparency and class.",
        "To add custom materials to the model.",
        [("Create a material 'Blue Glass' with 40% transparency and blue colour",
          "Material 'Blue Glass' created: colour (0,100,200), transparency 40%")])

    pdf.tool_card("duplicate_material",
        "Duplicate an existing material with all its properties.",
        "To create variants of existing materials.",
        [("Duplicate 'Concrete' as 'Special Concrete'",
          "Material duplicated with all assets (appearance, structural, thermal)")])

    pdf.tool_card("delete_material",
        "Delete a material from the model.",
        "To remove unused materials.",
        [("Delete material 'Test Material'",
          "Revit confirmation -> Material deleted")])

    pdf.tool_card("set_material_properties",
        "Modify properties of existing materials.",
        "To update colours, transparency or other properties.",
        [("Change the concrete colour to dark grey",
          "Colour updated: (100,100,100)"),
         ("Set glass transparency to 50%",
          "Transparency updated: 50%")])

    pdf.tool_card("get_compound_structure",
        "Show the compound structure (layer composition) of walls, floors or roofs.",
        "To check the constructive composition of a type.",
        [("Show the compound structure of 'External Wall 300mm'",
          "4 layers: Plaster 15mm | Brick 120mm | Insulation 80mm | Plasterboard 12.5mm"),
         ("What is the composition of the default floor type?",
          "3 layers: Screed 60mm | Concrete 150mm | Waterproofing 5mm")])

    pdf.tool_card("set_compound_structure",
        "Modify the compound structure of walls, floors or roofs. Add, remove or replace layers.",
        "To change insulation thickness, add a finish coat, etc.",
        [("Add an 80mm insulation layer to the wall",
          "Layer added: Insulation 80mm, position 3/5"),
         ("Replace the finish with 12.5mm plasterboard",
          "Layer 1 replaced: Plasterboard 12.5mm")])

    # =======================================================
    # 11 - LINKED FILES
    # =======================================================
    pdf.section_title("11", "Linked Files")

    pdf.tool_card("get_linked_file_instances",
        "List all linked Revit files with their status.",
        "To get an overview of links in the model.",
        [("Show all linked files",
          "3 links: Structure.rvt (loaded), MEP.rvt (loaded), Architecture_OLD.rvt (unloaded)")])

    pdf.tool_card("manage_links",
        "List, reload or unload linked files.",
        "To manage link status without using Revit's 'Manage Links' dialog.",
        [("Reload all linked files",
          "3 links reloaded successfully"),
         ("Unload link 'Architecture_OLD.rvt'",
          "Link unloaded from memory")])

    pdf.tool_card("add_linked_file",
        "Add a new Revit file as a link.",
        "To link a new model to the project.",
        [("Link the structural model from this path",
          "Link added: 'Structure.rvt', origin position")])

    pdf.tool_card("reload_linked_file_from",
        "Reload a link from a different path.",
        "When the linked file has been moved or renamed.",
        [("Reload the structural link from the new path",
          "Link reloaded from new path, 234 elements updated")])

    pdf.tool_card("get_link_transform",
        "Show the position and rotation of a link instance.",
        "To verify link alignment.",
        [("Where is the structural link?",
          "Position: (0.0, 0.0, 0.0) | Rotation: 0 degrees | System: Shared")])

    pdf.tool_card("align_link_to_host",
        "Align a link to the origin or shared coordinates.",
        "To correctly reposition a misaligned link.",
        [("Align the structural link to shared coordinates",
          "Link aligned to shared coordinate system")])

    pdf.tool_card("move_link_instance",
        "Move a link instance in the model.",
        "To reposition a link precisely.",
        [("Move the MEP link 5 metres east",
          "Link moved: translation X=5.0m")])

    pdf.tool_card("pin_unpin_link_instance",
        "Pin or unpin link instances.",
        "To protect links from accidental movement.",
        [("Pin all links",
          "3 instances pinned"),
         ("Unpin the structural link to reposition it",
          "1 instance unpinned")])

    pdf.tool_card("highlight_linked_element",
        "Highlight and zoom to a specific element inside a link, with optional section box.",
        "To visually locate an element in a linked model.",
        [("Show me element 789 in the structural link",
          "Element highlighted, section box created with 2m offset")])

    pdf.tool_card("get_selected_linked_elements",
        "Read information on selected elements that belong to links.",
        "When you select link elements in Revit and want to analyse them.",
        [("What do I have selected in the links?",
          "2 elements from link 'Structure.rvt': Beam HEB300 (ID: 45678), Column HEB240 (ID: 45690)")])

    # =======================================================
    # 12 - RENAME AND NUMBERING
    # =======================================================
    pdf.section_title("12", "Rename and Numbering")

    pdf.tool_card("batch_rename",
        "Rename elements in bulk with find-and-replace, prefix, or suffix.",
        "To standardise names of views, sheets, levels, rooms.",
        [("Rename all views replacing 'OLD' with 'NEW'",
          "5 views renamed: 'OLD Level 1' -> 'NEW Level 1'..."),
         ("Add prefix 'QC-' to all room names",
          "85 rooms renamed: 'Office' -> 'QC-Office'...")])

    pdf.tool_card("rename_families",
        "Rename families and optionally their types.",
        "To standardise family names in the model.",
        [("Add prefix 'STD-' to all door families",
          "8 families renamed: 'M_Single-Flush' -> 'STD-M_Single-Flush'..."),
         ("Replace 'Generic' with 'Custom' in family names",
          "3 families renamed")])

    pdf.tool_card("renumber_elements",
        "Automatically number elements in sequence: rooms, doors, windows, parking.",
        "To assign sorted numbers after placing elements.",
        [("Number all rooms starting from 101",
          "25 rooms numbered: 101, 102, 103... sorted by position"),
         ("Number doors by room in alphabetical order",
          "Doors numbered: D-001 (Archive), D-002 (Corridor), D-003 (Office 1)...")])

    # =======================================================
    # 13 - ADVANCED PARAMETERS
    # =======================================================
    pdf.section_title("13", "Advanced Parameters")

    pdf.tool_card("bulk_modify_parameter_values",
        "Modify a parameter in bulk: set value, prefix, suffix, find-and-replace, clear.",
        "To update the same parameter on many elements at once.",
        [("Set Manufacturer to 'ACME' for all doors",
          "dryRun: 156 doors would be updated. Proceed? -> 156 doors updated"),
         ("Add prefix 'STR-' to the Mark of all beams",
          "47 beams updated: 'B-01' -> 'STR-B-01'...")])

    pdf.tool_card("sync_csv_parameters",
        "Synchronise parameter values from CSV data (array of elementId + parameters).",
        "To import data from spreadsheets or external databases.",
        [("Update room numbers from this CSV data",
          "dryRun: 25 rooms would be updated. -> 25 updated successfully")])

    pdf.tool_card("import_from_excel",
        "Import parameter values from an Excel file into the Revit model.",
        "To bulk-update parameters by reading data from an externally prepared Excel sheet.",
        [("Import room data from the Excel file",
          "dryRun: 42 rooms would be updated from 'Room_Data.xlsx'. -> 42 updated successfully"),
         ("Update door parameters from the Excel file",
          "Imported: 156 values on 52 doors, 3 errors (ID not found)")],
        tips="Always run with dryRun first to verify data matching.")

    pdf.tool_card("transfer_parameters",
        "Copy parameter values from a source element to other elements.",
        "To propagate properties from a 'template' element to similar ones.",
        [("Copy all parameters from door 123 to doors 456, 789",
          "dryRun: 8 parameters would be copied to 2 elements -> Complete")])

    pdf.tool_card("add_prefix_suffix",
        "Add a prefix or suffix to parameter values.",
        "To add codes or indicators to existing parameters.",
        [("Add 'REV-' as prefix to all room numbers",
          "25 rooms updated: '101' -> 'REV-101'...")])

    pdf.tool_card("clear_parameter_values",
        "Clear the value of a parameter for a category/scope.",
        "To reset parameters before a new data entry.",
        [("Clear Comments for all walls",
          "68 walls: Comments parameter cleared")])

    pdf.tool_card("add_shared_parameter",
        "Add a shared parameter to one or more categories.",
        "To add custom parameters to the model.",
        [("Add shared parameter 'QC Status' to Doors and Windows categories",
          "Parameter 'QC Status' added as instance to Doors and Windows")])

    pdf.tool_card("manage_project_parameters",
        "List, create or delete project parameters.",
        "To manage project-specific parameters.",
        [("List all project parameters",
          "18 parameters: QC Status (Text), Check Date (Text), Inspector (Text)..."),
         ("Create a parameter 'Inspector' of type Text for Rooms",
          "Parameter created: 'Inspector', type Text, instance, applied to Rooms")])

    pdf.tool_card("get_shared_parameters",
        "List shared parameters in the model.",
        "To check which shared parameters are available.",
        [("List shared parameters for the Doors category",
          "5 shared parameters: Fire Rating, Sound Insulation, QC Status...")])

    pdf.tool_card("export_shared_parameter_file",
        "Export shared parameter definitions to a .txt file.",
        "To share parameters between projects.",
        [("Export shared parameters to a file",
          "File exported: SharedParameters.txt, 18 parameters")])

    # =======================================================
    # 14 - ANALYSIS AND QUALITY
    # =======================================================
    pdf.section_title("14", "Analysis and Quality")

    pdf.tool_card("check_model_health",
        "Quick model health check with score and main issues.",
        "First thing in the morning or before a deliverable.",
        [("What is the model health?",
          "Score: 78/100 | 3 issues: 45 warnings, 12 unused families, 3 heavy views"),
         ("Is the model ready for delivery?",
          "Score: 92/100 | 1 minor issue: 5 empty tags")])

    pdf.tool_card("analyze_model_statistics",
        "Detailed analysis: element count by category, most used types.",
        "To understand the composition and complexity of the model.",
        [("How many elements are in the model?",
          "Total: 12,456 elements | Walls: 347 | Doors: 156 | Windows: 89 | Rooms: 85..."),
         ("Show statistics in compact format",
          "Compact: 12.4K elements, 347 walls, 156 doors, 89 windows")])

    pdf.tool_card("get_warnings",
        "Read active warning messages in the model.",
        "For quality control and problem resolution.",
        [("Show me the first 10 warnings",
          "10 warnings: 4 'Elements overlap', 3 'Room height below minimum', 2 'Missing connection'..."),
         ("Are there any errors in the model?",
          "0 errors, 45 warnings. Most frequent: wall overlap (12), connections (8)")])

    pdf.tool_card("audit_families",
        "Check loaded families: size, instance count, unused families.",
        "To identify heavy or removable families.",
        [("Check door families",
          "8 families, 24 types, 156 instances | 2 unused types: 'Glass Door' and 'Revolving'"),
         ("Find unused families in the model",
          "15 families with no instances, occupy 12MB. Want to purge?")])

    pdf.tool_card("clash_detection",
        "Detect geometric clashes between two element categories.",
        "For inter-discipline coordination.",
        [("Check clashes between beams and ducts",
          "8 clashes found: Beam B-12 vs Duct D-05 (overlap 45mm)..."),
         ("Are there any walls intersecting columns?",
          "3 clashes: Wall M-23 vs Column P-05 (penetration 120mm)...")])

    pdf.tool_card("lines_per_view_count",
        "Count lines per view to identify heavy views.",
        "To find views that slow down the model.",
        [("Which views have the most lines?",
          "Top 5: Detail 42 (15,230 lines), Ground Floor (8,450), Section A (6,200)...")])

    pdf.tool_card("list_family_sizes",
        "List families by instance count or types.",
        "To identify the most used or heaviest families.",
        [("What are the 20 most used families?",
          "Top 20: Basic Wall (347 inst.), M_Single-Flush (124 inst.), Fixed Window (89 inst.)...")])

    pdf.tool_card("get_phases",
        "Show project phases and phase filters.",
        "To understand the project's temporal structure.",
        [("Show project phases",
          "3 phases: Existing (1), Demolition (2), New Construction (3) | 4 phase filters")])

    pdf.tool_card("get_worksets",
        "List worksets with element count.",
        "To verify workset organisation.",
        [("Show model worksets",
          "5 worksets: Architecture (4,500 el.), Structure (2,100 el.), MEP (1,800 el.)...")])

    # =======================================================
    # 15 - MODEL CLEANUP
    # =======================================================
    pdf.section_title("15", "Model Cleanup")

    pdf.tool_card("purge_unused",
        "Delete unused families, types and materials from the model.",
        "To reduce file size and improve performance.",
        [("What can I delete from the model?",
          "dryRun: 15 families, 23 types, 8 materials can be deleted (-12MB)"),
         ("Clean up everything that is not used",
          "Revit confirmation -> 46 elements deleted, file reduced by 12MB")],
        warns="Powerful operation. Always use dryRun first to verify.")

    pdf.tool_card("cad_link_cleanup",
        "Find and remove imported or linked CAD files.",
        "To remove DWG/DXF that are weighing down the model.",
        [("List all CAD imports in the model",
          "8 imports: floorplan.dwg (Ground Floor), detail.dwg (Section A)..."),
         ("Delete all DWG imports",
          "Confirmation -> 8 CAD imports removed")])

    # =======================================================
    # 16 - COMPOSITE WORKFLOWS
    # =======================================================
    pdf.section_title("16", "Composite Workflows")
    pdf.para("These tools combine multiple operations in a single command for complex tasks.")

    pdf.tool_card("workflow_model_audit",
        "Complete model audit: health + warnings + family analysis in one call.",
        "For a complete quality check before delivery.",
        [("Run a full model audit",
          "Report: Score 82/100 | 45 warnings (12 critical) | 15 unused families | 3 heavy views"),
         ("Audit with detailed warnings and families",
          "Detailed report: warnings by category, families by size, recommendations")])

    pdf.tool_card("workflow_clash_review",
        "Detect clashes and automatically create a 3D view with section box for review.",
        "When you need to visualise clashes, not just count them.",
        [("Check beam-duct clashes with a 3D view",
          "8 clashes found. 3D view 'Clash Review - Beams vs Ducts' created with section box"),
         ("Review wall-column clashes",
          "3 clashes found, 3D view created for each cluster")])

    pdf.tool_card("workflow_data_roundtrip",
        "Export data to Excel, edit externally, and reimport into the model.",
        "For bulk parameter updates via spreadsheet.",
        [("Export door data to Excel, I'll edit it and reimport",
          "File exported: Doors_Export.xlsx, 156 rows, 8 columns. Edit it and let me know when to reimport."),
         ("Reimport the modified Excel file",
          "dryRun: 23 doors would be updated, 2 parameters per door. Proceed?")])

    pdf.tool_card("workflow_room_documentation",
        "Automatically create sections for every room on a level.",
        "To quickly generate room documentation.",
        [("Create sections for all rooms on Level 1",
          "25 sections created, one per room. Names: 'Section - Office 101', 'Section - Corridor 102'...")])

    pdf.tool_card("workflow_sheet_set",
        "Create a complete set of sheets with title block in a single operation.",
        "To quickly set up a project drawing set.",
        [("Create sheets A-101 to A-105 with the company title block",
          "5 sheets created: A-101 Ground Floor | A-102 Level 1 | A-103 Level 2 | A-104 Sections | A-105 Elevations")])

    # =======================================================
    # 17 - SECURITY
    # =======================================================
    pdf.section_title("17", "Security")
    pdf.para("RevitCortex includes several protections to prevent accidental damage to the model.")

    pdf.h2("Confirmation dialog")
    pdf.para("All destructive operations (delete, purge, modify parameters, rename) show "
             "a native Revit confirmation dialog before executing. If you cancel, the tool returns "
             "a 'Cancelled' error and nothing is modified.")

    pdf.h2("Code sandbox")
    pdf.para("The send_code_to_revit tool validates C# code before executing it. "
             "The following namespaces are forbidden and cause a 'PermissionDenied' error:")
    pdf.code("System.IO            - filesystem access\n"
             "System.Net           - network access\n"
             "System.Diagnostics   - system processes\n"
             "Microsoft.Win32      - Windows registry\n"
             "System.Reflection.Emit - dynamic code generation\n"
             "System.Runtime.InteropServices - native interop")

    pdf.h2("Read-only mode")
    pdf.para("By setting readOnlyMode: true in ~/.revitcortex/settings.json, "
             "all write tools are blocked. Only read operations "
             "(get_, list_, find_, analyze_, check_, export_, audit_) remain active.")

    pdf.h2("Audit log")
    pdf.para("Every tool execution is recorded in ~/.revitcortex/audit.jsonl "
             "with timestamp, tool name, result and number of elements involved.")

    # =======================================================
    # 18 - SESSION OPTIMISATION
    # =======================================================
    pdf.section_title("18", "Session Optimisation")

    pdf.h2("Recommended session patterns")
    pdf.para("Don't do everything in one conversation. Divide work into short, focused sessions:")

    pdf.h3("Morning session (2-3 calls)")
    pdf.prompt("", "What is the model health? Show me the 10 main warnings.")
    pdf.text_small("Tools: check_model_health + get_warnings. Cost: ~800 response tokens.")

    pdf.h3("Parameter session (3-4 calls)")
    pdf.prompt("", "Export door data, then set manufacturer 'ACME' for Fire Door type doors.")
    pdf.text_small("Tools: export_elements_data + bulk_modify (dryRun) + bulk_modify. Cost: ~2,000 tokens.")

    pdf.h3("Documentation session (3 calls)")
    pdf.prompt("", "Create a door schedule by room, export it as CSV, and create sheets A-101 to A-105.")
    pdf.text_small("Tools: create_preset_schedule + export_schedule + workflow_sheet_set. Cost: ~1,500 tokens.")

    pdf.h2("Golden rules")
    pdf.para("1. The first get_project_info must be complete. Subsequent calls must filter.\n"
             "2. Use maxWarnings: 10 for quick checks, not the default 500.\n"
             "3. With dryRun, read only modifiedCount/skippedCount, not the full list.\n"
             "4. When context exceeds ~15,000 response tokens, start a new conversation.\n"
             "5. Don't mix QA tasks with authoring in the same long session.\n"
             "6. Use the most targeted tool: check_model_health before analyze_model_statistics.")

    pdf.h2("Performance")
    pdf.para("- Read operations: safe in parallel (5+)\n"
             "- Write operations: max 3-4 in parallel\n"
             "- Heavy queries (analyze_model_statistics, purge_unused): run individually\n"
             "- create_view 3D: particularly heavy, avoid alongside other writes")

    pdf.h2("Language and localisation")
    pdf.para("Revit translates category and parameter names based on language. "
             "RevitCortex auto-detects the language. Always use OST_ codes "
             "(language-independent) for categories:\n\n"
             "OST_Walls = Walls / Muri / Murs / Wande\n"
             "OST_Doors = Doors / Porte / Portes / Turen\n"
             "OST_Windows = Windows / Finestre / Fenetres / Fenster\n"
             "OST_Rooms = Rooms / Vani / Pieces / Raume\n"
             "OST_Floors = Floors / Pavimenti / Sols / Geschossdecken\n"
             "OST_StructuralFraming = Structural Framing\n"
             "OST_StructuralColumns = Structural Columns")

    pdf.h2("Storage and analysis tools")
    pdf.para("These tools operate server-side to store data between sessions:")

    pdf.tool_card("store_project_data",
        "Save project metadata for retrieval in future sessions.",
        "To keep a history of analysed projects.",
        [("Save information about this project",
          "Project 'Building A' saved to local database")])

    pdf.tool_card("store_room_data",
        "Save a snapshot of room data.",
        "To compare rooms over time.",
        [("Save the room data for later comparison",
          "85 rooms saved for project 'Building A'")])

    pdf.tool_card("query_stored_data",
        "Query previously saved data.",
        "To retrieve data from past sessions.",
        [("Show saved projects",
          "3 projects: Building A, School B, Hospital C")])

    pdf.tool_card("report_token_usage",
        "Report on MCP tool usage.",
        "To understand which tools you use most.",
        [("Which tools have I used most this week?",
          "Top 5: get_element_parameters (45x), filter_by_parameter_value (23x), export_elements_data (18x)...")])

    pdf.tool_card("analyze_journal",
        "Analyse Revit journal files for diagnostics.",
        "To investigate crashes, memory issues, or abnormal behaviour.",
        [("Analyse the last 3 Revit sessions",
          "Session 1: 45 min, 3.2GB RAM peak, 12 transactions | Session 2: crash after 23 min...")])

    # =======================================================
    # 19 - IFC
    # =======================================================
    pdf.section_title("19", "IFC - Import, Export and Native Reconstruction")
    pdf.para("IFC (Industry Foundation Classes) is the open format for BIM interoperability. "
             "RevitCortex offers three tool groups: "
             "import/linking, configurable export, "
             "and native reconstruction - the most advanced workflow that converts IFC elements "
             "into editable native Revit elements.")

    pdf.h2("Verification and diagnostics")

    pdf.tool_card("ifc_get_capabilities",
        "Check IFC support for this Revit installation: supported versions, revit-ifc add-in.",
        "Before working with IFC, to know what is available.",
        [("Which IFC versions does this Revit installation support?",
          "IFC 2x3, IFC 4, IFC 4x3 supported | revit-ifc add-in: v24.1.0 | Schema: ifcXML supported"),
         ("Does Revit support IFC 4.3?",
          "IFC 4x3 supported (add-in v24.1.0). Export and import available.")])

    pdf.tool_card("ifc_validate_request",
        "Validate an IFC file before importing: checks path, extension and schema version.",
        "Before importing or linking an IFC file to catch errors early.",
        [("Check if file C:/Models/Structure.ifc is valid",
          "OK: file found, .ifc extension valid, IFC 2x3 schema detected"),
         ("Verify the IFC file before linking it to the project",
          "OK: IFC 4 schema, 45MB size, valid structure")])

    pdf.h2("Import and linking")

    pdf.tool_card("ifc_link",
        "Link an IFC file to the project as an external link (stays updatable).",
        "When you want to reference the IFC model without embedding it in the project.",
        [("Link the structural IFC model from C:/Models/Structure.ifc",
          "IFC link added: 'Structure.ifc', 1,245 elements, shared coordinate system"),
         ("Add the MEP IFC file as a link",
          "IFC link 'MEP.ifc' linked: 3,102 elements, origin position")])

    pdf.tool_card("ifc_reload_link",
        "Reload an existing IFC link, optionally from a new path.",
        "When the IFC file has been updated by the structural or MEP consultant.",
        [("Reload the structural IFC link",
          "Link 'Structure.ifc' reloaded: 1,287 elements (+42 vs previous version)"),
         ("Reload the MEP link from the new updated path",
          "Link reloaded from new path, 3,150 elements updated")])

    pdf.tool_card("ifc_open_or_import",
        "Open an IFC file as a new Revit project or import it into the active document.",
        "When you want to permanently incorporate the IFC model into the project.",
        [("Open the IFC file as a new Revit project",
          "File opened as new Revit project, 2,456 elements converted to DirectShape"),
         ("Import the IFC file into the current project",
          "1,245 DirectShapes imported into the active document")])

    pdf.h2("Export")

    pdf.tool_card("ifc_list_export_configurations",
        "List available IFC export configurations (built-in and custom).",
        "Before exporting, to choose the right configuration.",
        [("What IFC configurations can I use for export?",
          "6 configurations: IFC2x3 Coordination View, IFC4 Reference View, IFC4 Design Transfer, GSA (2010), COBie 2.4, Custom_Arch")])

    pdf.tool_card("ifc_get_export_configuration",
        "Show full details of a specific IFC export configuration.",
        "To verify a configuration's parameters before using it.",
        [("Show details of the 'IFC4 Design Transfer' configuration",
          "Schema: IFC4 | Classification: OmniClass | Exports: walls, doors, windows, spaces | Include quantities: yes")])

    pdf.tool_card("ifc_export_basic",
        "Export the model to IFC with basic options: schema version, output path.",
        "For quick export without complex configurations.",
        [("Export the model as IFC 4 to folder C:/Deliverables",
          "Export complete: Building_A.ifc (45MB), IFC4 schema, 3,456 elements"),
         ("Export as IFC 2x3 for compatibility with legacy systems",
          "IFC 2x3 export complete: 3,201 elements, compatible format")])

    pdf.tool_card("ifc_export_with_configuration",
        "Export using a named configuration with the option of specific overrides.",
        "For professional export with controlled parameters.",
        [("Export using the 'IFC4 Design Transfer' configuration with OmniClass classification",
          "Export with config 'IFC4 Design Transfer': 3,456 elements, 156 spaces, OmniClass applied"),
         ("Export only Ground Floor with the COBie 2.4 configuration",
          "COBie export complete: 245 Ground Floor elements, COBie sheet generated")])

    pdf.tool_card("ifc_set_family_mapping_file",
        "Set a mapping file to associate Revit families with the correct IFC types.",
        "To ensure families are exported with the appropriate IFC type.",
        [("Set the family mapping file from C:/Config/FamilyMapping.txt",
          "Mapping file set: 45 family->IFC type association rules loaded")])

    pdf.h2("Native reconstruction (IFC -> native Revit)")
    pdf.para("When you import an IFC file, Revit creates DirectShapes - non-editable geometric objects. "
             "The reconstruction workflow analyses these elements and converts them into native Revit elements "
             "(walls, floors, beams, doors) that are fully editable.")
    pdf.tip("Recommended workflow: analyse -> list candidates -> rebuild by category -> cut openings -> place doors/windows -> compare -> tag non-reconstructable.")

    pdf.tool_card("ifc_analyze_rebuildability",
        "Analyse IFC DirectShapes in the model and calculate native reconstruction feasibility for each.",
        "First step of the reconstruction workflow: understand how many elements can be converted.",
        [("Analyse the imported IFC model: how many elements can I reconstruct?",
          "Analysis complete: 1,245 DirectShapes | Walls: 312 (89% reconstructable) | Floors: 45 (95%) | Beams: 67 (78%) | Non-reconstructable: 123 (10%)"),
         ("Check reconstructability for walls only",
          "312 walls analysed: 278 reconstructable (confidence >80%), 34 complex (possible geometry loss)")])

    pdf.tool_card("ifc_list_rebuild_candidates",
        "List elements with reconstruction confidence above a specified threshold.",
        "To decide which elements to reconstruct, starting with the most reliable.",
        [("Show all reconstructable elements with at least 85% confidence",
          "847 candidates: 278 walls, 43 floors, 62 beams, 45 columns (min confidence: 85%)"),
         ("Which walls can I safely reconstruct?",
          "278 walls with confidence >80%: 245 simple rectangular, 33 with irregular profile")])

    pdf.tool_card("ifc_rebuild_walls",
        "Rebuild walls from IFC DirectShapes as native Revit walls with correct thickness and type.",
        "After analysis, to convert IFC walls into editable Revit walls.",
        [("Rebuild all walls from the imported IFC",
          "278 walls rebuilt as native Revit walls: 'Basic Wall 200mm' (142), 'Basic Wall 300mm' (89), others (47)"),
         ("Rebuild only ground floor walls",
          "68 ground floor walls rebuilt, base level set to 'Ground Floor'")])

    pdf.tool_card("ifc_rebuild_floors",
        "Rebuild floors from IFC DirectShapes as native Revit floors.",
        "To convert IFC floors into editable elements with compound structure.",
        [("Rebuild floors from the IFC model",
          "43 floors rebuilt: type 'Generic Floor 200mm', total area 4,560 sqm"),
         ("Rebuild slabs with the architectural floor type",
          "43 floors rebuilt with type 'Architectural Floor 150mm'")])

    pdf.tool_card("ifc_rebuild_roofs",
        "Rebuild roofs from IFC DirectShapes as native Revit roofs.",
        "To convert the IFC roof into an editable element with slopes.",
        [("Rebuild the roof from the IFC file",
          "5 roof panels rebuilt as native Revit roof, slopes calculated automatically"),
         ("Rebuild the flat roof",
          "1 flat roof rebuilt: type 'Flat Roof 300mm', area 980 sqm")])

    pdf.tool_card("ifc_rebuild_structural_members",
        "Rebuild columns and beams from IFC DirectShapes as native Revit structural elements.",
        "To convert the IFC structural frame into analysable and editable elements.",
        [("Rebuild all structural framing from IFC",
          "Complete: 45 HEB240/300 columns rebuilt, 67 IPE300/400 beams rebuilt"),
         ("Rebuild only Level 2 beams",
          "22 Level 2 beams rebuilt: HEB300 (12), IPE400 (10), average span 7.2m")])

    pdf.tool_card("ifc_rebuild_openings",
        "Cut openings in rebuilt walls and floors, matching the original IFC openings.",
        "After rebuilding walls and floors, to create the correct openings.",
        [("Cut openings in rebuilt walls",
          "234 openings cut: 156 for doors, 78 for windows"),
         ("Also create openings in rebuilt floors",
          "12 openings for stairwells and services cut in floors")])

    pdf.tool_card("ifc_rebuild_family_instances",
        "Place doors and windows in rebuilt openings, using native Revit families.",
        "Last reconstruction step: populate openings with native elements.",
        [("Place doors and windows in the rebuilt openings",
          "156 doors placed (M_Single-Flush 900x2100), 78 windows placed (Fixed 1200x1500)"),
         ("Place doors using 'Fire Door EI60' family for fire-rated openings",
          "45 fire doors placed, 111 standard doors")])

    pdf.tool_card("ifc_compare_original_vs_rebuilt",
        "Compare volume and geometry between original IFC elements and rebuilt Revit elements.",
        "To verify reconstruction fidelity before deleting the DirectShapes.",
        [("Compare rebuilt elements with original IFC",
          "Average fidelity: 97.3% | Walls: 98.1% | Floors: 96.8% | 12 elements with >5% deviation flagged"),
         ("Verify fidelity of rebuilt walls",
          "278 walls compared: 265 (95%) fidelity >95%, 13 with geometric divergence to check")])

    pdf.tool_card("ifc_tag_unreconstructable_elements",
        "Tag IFC elements that cannot be rebuilt as native (geometry too complex).",
        "To document which elements remain as DirectShapes and must be handled manually.",
        [("Tag all elements I could not reconstruct",
          "123 elements tagged as 'IFC_Non_Reconstructable': 45 stairs, 34 ramps, 44 complex geometries"),
         ("Add a 'Reconstruction Failure Reason' parameter to non-rebuilt elements",
          "123 elements: parameter added with reason (stair, curved geometry, etc.)")])

    # =======================================================
    # APPENDIX - PROMPT LIBRARY
    # =======================================================
    pdf.add_page()
    pdf.set_font("Helvetica", "B", 22)
    pdf.set_text_color(*C_PRIMARY)
    pdf.cell(0, 14, "Appendix - Prompt Library", new_x="LMARGIN", new_y="NEXT", align="C")
    pdf.ln(2)
    pdf.set_font("Helvetica", "", 10)
    pdf.set_text_color(*C_GRAY)
    pdf.multi_cell(0, 5.5, "Ready-to-use prompts organised by scenario. Copy, adapt to your project and paste into Claude.")
    pdf.ln(4)

    def prompt_row(label, text):
        pdf.set_font("Helvetica", "B", 8)
        pdf.set_text_color(*C_ACCENT)
        pdf.cell(38, 5.5, label)
        pdf.set_font("Helvetica", "I", 8.5)
        pdf.set_text_color(0, 70, 40)
        pdf.set_fill_color(*C_BG_PROMPT)
        pdf.multi_cell(0, 5.5, f'"{text}"', fill=True)
        pdf.ln(0.5)

    scenarios = [
        ("ORIENTING IN A NEW MODEL", [
            ("Full overview", "What is this project? Show me levels, worksets, phases and linked files."),
            ("Quick stats", "How many elements are there by category? Show me the top 10 categories."),
            ("Active view", "Which view am I in? How many elements are visible?"),
            ("Model health", "Do a quick model health check and show me the 5 most important warnings."),
        ]),
        ("FINDING AND FILTERING ELEMENTS", [
            ("By category", "Find all [structural columns] in the model, show me type and level."),
            ("By parameter", "Find all doors where the 'Fire Rating' field is empty."),
            ("By value", "Which walls on Level 1 have thickness greater than 250mm?"),
            ("In active view", "List all visible walls in this view with type and length."),
            ("Selected element", "What do I have selected? Show me all parameters."),
        ]),
        ("MODIFYING PARAMETERS", [
            ("Single element", "Set the Mark of door 12345 to 'D-001'."),
            ("Bulk (safe)", "How many doors would be modified if I set Manufacturer to 'ACME'? (preview)"),
            ("Bulk (execute)", "OK, proceed: set Manufacturer 'ACME' for all doors."),
            ("From Excel", "Import the parameter values from Excel file 'DoorData.xlsx' (show me preview first)."),
            ("Rename", "Rename all views replacing 'Copy of ' with '' (remove the prefix)."),
            ("Number rooms", "Number all rooms starting from 101, ordered left to right."),
        ]),
        ("CREATING ELEMENTS", [
            ("Wall", "Create a 'Basic Wall 200mm' wall from point (0,0) to point (8,0) at Ground Floor."),
            ("Floor", "Create an architectural floor in room 205 with type 'Concrete 150mm'."),
            ("Door", "Place a 'M_Single-Flush 900x2100' door in wall 5678 at 1.5m from the left end."),
            ("Room", "Create a room 'Director's Office' number 101 on Level 1 at coordinates (12, 8)."),
            ("Level", "Create Level 3 at elevation 10.5 metres."),
            ("Grid", "Create a 4x3 grid with 6m spacing, axes A-D and 1-3."),
        ]),
        ("VIEWS AND SHEETS", [
            ("Floor plan", "Create a floor plan of Level 2 at scale 1:100."),
            ("Section", "Create a section cutting the building from west to east at Ground Floor."),
            ("Isolated 3D", "Create a 3D view with section box around the selected elements."),
            ("Batch sheets", "Create sheets A-101, A-102, A-103 with 'Company Titleblock'."),
            ("Place view", "Put the Level 1 plan on sheet A-101, centred."),
            ("Views for rooms", "Create a section for every room on Level 1."),
            ("Export DWG", "Export all sheets to DWG in folder C:/Deliverables."),
        ]),
        ("SCHEDULES AND DATA", [
            ("Quick schedule", "Create a door schedule with Type Name, Level and Fire Rating."),
            ("Room schedule", "Create a room list with name, number, area and department."),
            ("Export CSV", "Export the door schedule as a CSV file."),
            ("Export Excel", "Export all door data to an Excel file on the desktop."),
            ("Read schedule", "Show me the first 20 rows of the 'Door Schedule'."),
        ]),
        ("QUALITY AND AUDIT", [
            ("Morning check", "What is the model health? Score and main issues."),
            ("Warnings", "Show me the 10 most frequent warnings in the model."),
            ("Clash detection", "Check clashes between beams and ducts. Create a 3D view for the clashes found."),
            ("Heavy families", "What are the 10 most used families? Are there any unused families?"),
            ("Pre-delivery", "Run a full audit: health, critical warnings, unused families, heavy views."),
        ]),
        ("MATERIALS AND COMPOUND STRUCTURES", [
            ("Material list", "List all materials in the model grouped by class."),
            ("Compound structure", "Show the layer composition of wall 'External Wall 300mm'."),
            ("Create material", "Create a material 'Silk-screen Glass' with 30% transparency and light grey colour."),
            ("Quantities", "How many sqm of concrete are used in the model?"),
        ]),
        ("IFC - IMPORT AND EXPORT", [
            ("Check support", "Which IFC versions does this Revit installation support?"),
            ("Validate file", "Check if file C:/Deliverables/Structure.ifc is valid before linking it."),
            ("Link IFC", "Link the structural IFC model from file C:/Models/Structure.ifc."),
            ("Export IFC4", "Export the model as IFC 4 to folder C:/Deliverables."),
            ("Config export", "Export using the 'IFC4 Design Transfer' configuration."),
        ]),
        ("IFC - NATIVE RECONSTRUCTION", [
            ("Analysis", "Analyse the imported IFC elements: how many can I rebuild as native Revit elements?"),
            ("Candidates", "List all elements with reconstruction confidence above 85%."),
            ("Rebuild walls", "Rebuild all walls from the imported IFC as native Revit walls."),
            ("Rebuild all", "Rebuild walls, floors, beams and columns. Then cut openings and place doors and windows."),
            ("Compare", "Compare the geometry of rebuilt elements with the original IFC. What is the average fidelity?"),
            ("Tag residuals", "Tag the IFC elements that cannot be rebuilt as native."),
        ]),
        ("CLEANUP AND PROJECT CLOSE", [
            ("Purge", "What can I delete from the model? Unused families, types and materials. (preview first)"),
            ("Stale CAD", "List all CAD imports in the model. Then delete those no longer needed."),
            ("Orphan views", "Show me views not placed on any sheet. Delete the working ones?"),
            ("Empty tags", "Remove all empty tags from the current view."),
            ("Worksets", "Move all doors to workset 'Architecture - Doors'."),
        ]),
        ("CUSTOM C# SCRIPT (Revit 2025+)", [
            ("Batch operation", "Use send_code_to_revit to rename all 'Generic' families adding the 'STD-' prefix."),
            ("Complex type", "Write C# code to create floor type 'FL_001' with 3 layers: ceramic 10mm, screed 60mm, concrete 150mm."),
            ("Advanced read", "Use send_code_to_revit to calculate total wall area by level and return a table."),
        ]),
    ]

    for scenario_title, prompts in scenarios:
        if pdf.get_y() > 255:
            pdf.add_page()
        pdf.ln(2)
        pdf.set_font("Helvetica", "B", 10)
        pdf.set_text_color(*C_PRIMARY)
        pdf.set_fill_color(230, 240, 255)
        pdf.cell(0, 7, f"  {scenario_title}", fill=True, new_x="LMARGIN", new_y="NEXT")
        pdf.ln(1)
        for label, text in prompts:
            prompt_row(label, text)
        pdf.ln(1)

    # -- Save --
    out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "docs", "RevitCortex_User_Guide_EN.pdf")
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    pdf.output(out_path)
    print(f"PDF generated: {out_path}")
    print(f"Pages: {pdf.page_no()}")


if __name__ == "__main__":
    build_pdf()
