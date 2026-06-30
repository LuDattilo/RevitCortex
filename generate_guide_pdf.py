#!/usr/bin/env python3
"""Generate RevitCortex User Guide PDF - practical, user-friendly format."""

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
            self.cell(0, 6, "RevitCortex - Guida Utente", align="L")
            self.cell(0, 6, f"Pagina {self.page_no()}", align="R", new_x="LMARGIN", new_y="NEXT")
            self.set_draw_color(*C_LIGHT_GRAY)
            self.line(10, 14, 200, 14)
            self.ln(4)

    def footer(self):
        self.set_y(-12)
        self.set_font("Helvetica", "I", 7)
        self.set_text_color(*C_LIGHT_GRAY)
        self.cell(0, 8, "RevitCortex v1.0.47 - AI Assistant for Autodesk Revit", align="C")

    def section_title(self, num, title):
        self.add_page()
        self.ln(8)
        self.set_font("Helvetica", "B", 28)
        self.set_text_color(*C_PRIMARY)
        self.cell(0, 14, f"{num}", new_x="LMARGIN", new_y="NEXT")
        self.set_font("Helvetica", "", 18)
        self.set_text_color(*C_DARK)
        self.cell(0, 10, title, new_x="LMARGIN", new_y="NEXT")
        self.set_draw_color(*C_PRIMARY)
        self.set_line_width(0.8)
        self.line(10, self.get_y() + 2, 80, self.get_y() + 2)
        self.set_line_width(0.2)
        self.ln(8)

    def h2(self, title):
        if self.get_y() > 250:
            self.add_page()
        self.ln(4)
        self.set_font("Helvetica", "B", 13)
        self.set_text_color(*C_PRIMARY)
        self.cell(0, 9, title, new_x="LMARGIN", new_y="NEXT")
        self.ln(2)

    def h3(self, title):
        if self.get_y() > 260:
            self.add_page()
        self.ln(2)
        self.set_font("Helvetica", "B", 10.5)
        self.set_text_color(*C_DARK)
        self.cell(0, 7, title, new_x="LMARGIN", new_y="NEXT")
        self.ln(1)

    def para(self, t):
        self.set_font("Helvetica", "", 9.5)
        self.set_text_color(*C_DARK)
        self.multi_cell(0, 5.2, t)
        self.ln(1)

    def text_small(self, t):
        self.set_font("Helvetica", "", 8.5)
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
        """Green box: what you say to Claude."""
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
        """Blue box: what Claude returns."""
        self.set_font("Helvetica", "", 8.5)
        self.set_text_color(30, 60, 120)
        self.set_fill_color(*C_BG_RESULT)
        w = self.w - self.l_margin - self.r_margin
        self.multi_cell(w, 4.8, f"  {t}", fill=True)
        self.ln(1)

    def tip(self, t):
        """Yellow tip box."""
        self.set_font("Helvetica", "B", 8)
        self.set_text_color(160, 120, 0)
        self.set_fill_color(*C_BG_TIP)
        w = self.w - self.l_margin - self.r_margin
        self.multi_cell(w, 4.8, f"  TIP: {t}", fill=True)
        self.ln(2)

    def warn(self, t):
        """Red warning box."""
        self.set_font("Helvetica", "B", 8)
        self.set_text_color(180, 40, 40)
        self.set_fill_color(*C_BG_WARN)
        w = self.w - self.l_margin - self.r_margin
        self.multi_cell(w, 4.8, f"  ATTENZIONE: {t}", fill=True)
        self.ln(2)

    def tool_card(self, name, what, when, prompts_results, tips=None, warns=None):
        """Full tool card with scenario, prompts, results, tips."""
        needed = 50 + len(prompts_results) * 20
        if self.get_y() + min(needed, 80) > 270:
            self.add_page()

        # Tool name bar
        self.set_draw_color(*C_PRIMARY)
        self.set_line_width(0.6)
        y_start = self.get_y()
        self.line(10, y_start, 10, y_start + 4)
        self.set_line_width(0.2)
        self.set_font("Courier", "B", 11)
        self.set_text_color(*C_PRIMARY)
        self.cell(0, 6, f"  {name}", new_x="LMARGIN", new_y="NEXT")

        # What it does
        self.set_font("Helvetica", "", 9)
        self.set_text_color(*C_DARK)
        self.multi_cell(0, 5, what)
        self.ln(1)

        # When to use
        if when:
            self.set_font("Helvetica", "B", 8)
            self.set_text_color(*C_GRAY)
            self.cell(0, 4, "Quando usarlo:", new_x="LMARGIN", new_y="NEXT")
            self.set_font("Helvetica", "I", 8.5)
            self.multi_cell(0, 4.5, when)
            self.ln(1)

        # Prompt-result pairs
        for i, (prompt_text, result_text) in enumerate(prompts_results):
            lbl = f"Esempio {i+1}:" if len(prompts_results) > 1 else "Esempio:"
            self.prompt(lbl, prompt_text)
            if result_text:
                self.result(result_text)

        if tips:
            self.tip(tips)
        if warns:
            self.warn(warns)

        # Separator
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
    pdf.cell(0, 12, "Guida Utente Completa", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(8)
    pdf.set_draw_color(*C_PRIMARY)
    pdf.set_line_width(0.8)
    pdf.line(60, pdf.get_y(), 150, pdf.get_y())
    pdf.set_line_width(0.2)
    pdf.ln(8)
    pdf.set_font("Helvetica", "", 12)
    pdf.set_text_color(*C_GRAY)
    pdf.cell(0, 7, "Assistente AI per Autodesk Revit", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 7, "149 Strumenti | Revit 2023-2027", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.cell(0, 7, "Supporto multilingua: EN, IT, FR, DE", align="C", new_x="LMARGIN", new_y="NEXT")
    pdf.ln(30)
    pdf.set_font("Helvetica", "I", 10)
    pdf.cell(0, 6, "Giugno 2026 - v1.0.47", align="C", new_x="LMARGIN", new_y="NEXT")

    # =======================================================
    # TABLE OF CONTENTS
    # =======================================================
    pdf.section_title("", "Indice")
    toc = [
        ("01", "Per iniziare", "Prerequisiti, installazione, primo collegamento"),
        ("02", "Come parlare a Revit", "Strategie di prompt, ruoli, template"),
        ("03", "Interrogare il modello", "Cercare elementi, filtrare, esportare dati"),
        ("04", "Creare elementi", "Posizionare porte, muri, pilastri, griglie, livelli"),
        ("05", "Modificare elementi", "Cambiare parametri, spostare, copiare, eliminare"),
        ("06", "Tipi e famiglie", "Duplicare tipi, cambiare tipo, gestire famiglie"),
        ("07", "Viste e tavole", "Creare piante, sezioni, 3D, tavole, viewport"),
        ("08", "Abachi e schedule", "Creare abachi, esportare, modificare campi"),
        ("09", "Annotazioni e tag", "Taggare stanze, quotare, note di testo, legende"),
        ("10", "Materiali e stratigrafie", "Gestire materiali, strutture composte"),
        ("11", "File collegati", "Navigare link, spostare, ricaricare, evidenziare"),
        ("12", "Rinomina e numerazione", "Rinominare in massa, numerare stanze/porte"),
        ("13", "Parametri avanzati", "Parametri condivisi, di progetto, trasferimento"),
        ("14", "Analisi e qualita'", "Health check, warning, clash, audit famiglie"),
        ("15", "Pulizia del modello", "Purge, pulizia CAD, tag vuoti"),
        ("16", "Workflow compositi", "Audit completo, data roundtrip, sheet set"),
        ("17", "Sicurezza", "Sandbox, read-only, audit log, conferme"),
        ("18", "Ottimizzazione sessione", "Pattern di sessione, gestione token"),
        ("19", "IFC", "Importazione, esportazione, ricostruzione nativa"),
        ("", "Appendice", "Libreria di prompt pronti all'uso per scenario"),
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
    # 01 - PER INIZIARE
    # =======================================================
    pdf.section_title("01", "Per iniziare")
    pdf.para("RevitCortex e' un assistente AI che ti permette di controllare Autodesk Revit usando il linguaggio naturale. "
             "Invece di navigare menu e finestre, parli con Claude e lui esegue le operazioni nel modello per te.")
    pdf.para("Puoi chiedere cose come:")
    pdf.prompt("", "Quanti muri ci sono nel modello e quali tipi sono usati?")
    pdf.prompt("", "Imposta il parametro 'Produttore' a 'ACME' per tutte le porte del Piano 1")
    pdf.prompt("", "Crea un abaco delle stanze con nome, numero, area e dipartimento")

    pdf.h2("Cosa ti serve")
    pdf.para("1. Autodesk Revit 2023, 2024, 2025, 2026 o 2027\n"
             "2. Claude Desktop con piano Pro o Max (scaricalo da claude.ai/download)\n"
             "   oppure Claude Code (CLI)\n"
             "3. Il pacchetto RevitCortex (ZIP con installer incluso)")

    pdf.h2("Installazione")
    pdf.para("Estrai lo ZIP di RevitCortex in una cartella a tua scelta, poi fai doppio clic su install.bat "
             "(oppure tasto destro su install.ps1 > Esegui con PowerShell). "
             "L'installer richiede i privilegi di amministratore e in pochi secondi:")
    pdf.para("  - Copia il plugin nelle cartelle Addins di tutte le versioni Revit rilevate\n"
             "  - Installa il server MCP in %USERPROFILE%\\.revitcortex\\server\\\n"
             "  - Configura automaticamente Claude Desktop o Claude Code a tua scelta")
    pdf.para("Non serve installare Node.js, Python o altri runtime: il server e' un eseguibile autonomo (.exe).")
    pdf.para("Al termine dell'installazione, la configurazione di Claude Desktop e' gia' pronta:")
    pdf.code('{\n  "mcpServers": {\n    "revitcortex": {\n'
             '      "command": "C:\\\\Users\\\\<nome>\\\\' + '.revitcortex\\\\server\\\\RevitCortex.Server.exe",\n'
             '      "args": []\n    }\n  }\n}')

    pdf.h2("La tua prima sessione")
    pdf.para("Segui questi passi nell'ordine:")
    pdf.para("1. Apri il tuo progetto Revit")
    pdf.para("2. Clicca 'Cortex Switch' nel ribbon di Revit per avviare il server")
    pdf.para("3. Riavvia Claude Desktop")
    pdf.para("4. Cerca l'icona del martello nella barra di input di Claude")
    pdf.para("5. Testa il collegamento:")
    pdf.prompt("Il tuo primo messaggio:", "Ciao Revit, sei collegato?")
    pdf.result("Claude chiamera' say_hello e ti rispondera' con un messaggio di conferma dal plugin.")
    pdf.para("6. Orientati nel modello:")
    pdf.prompt("Secondo messaggio:", "Cos'e' questo progetto? Mostrami livelli, collegamenti e workset")
    pdf.result("Riceverai un riepilogo: nome progetto, autore, livelli con quote, link, workset attivi.")
    pdf.tip("Il primo get_project_info della sessione deve essere completo. Le chiamate successive possono filtrare.")

    # =======================================================
    # 02 - COME PARLARE A REVIT
    # =======================================================
    pdf.section_title("02", "Come parlare a Revit")
    pdf.para("Non devi imparare nomi di comandi o sintassi speciali. Parla come parleresti a un collega BIM esperto. "
             "Claude capisce il contesto e sceglie lo strumento giusto automaticamente.")

    pdf.h2("Imposta un ruolo all'inizio")
    pdf.para("Inizia ogni sessione dicendo a Claude chi e':")
    pdf.prompt("Ruolo consigliato:", "Sei un esperto BIM che lavora in Revit. Usa solo gli strumenti MCP disponibili. "
               "Chiedi conferma prima di fare modifiche importanti.")
    pdf.result("Claude si comportera' in modo piu' preciso nella scelta degli strumenti e chiedera' conferma per le operazioni distruttive.")

    pdf.h2("Sii specifico")
    pdf.para("Piu' dettagli dai, migliori saranno i risultati:")
    pdf.prompt("Vago (sconsigliato):", "Mostrami i muri")
    pdf.prompt("Specifico (consigliato):", "Mostrami tutti i muri di tipo 'Muro di base: Cemento 200mm' al Piano 1 con la resistenza al fuoco")
    pdf.tip("Specifica sempre: categoria + tipo + livello + parametri di interesse.")

    pdf.h2("Anteprima prima di modificare")
    pdf.para("Per operazioni in massa, chiedi sempre un'anteprima prima:")
    pdf.prompt("Passo 1 - Anteprima:", "Quali porte verrebbero modificate se cambio il Contrassegno a tutte le porte del Piano 2?")
    pdf.result("Claude eseguira' una simulazione (dryRun) mostrando quanti elementi sarebbero coinvolti.")
    pdf.prompt("Passo 2 - Conferma:", "OK, procedi con la modifica")
    pdf.result("Revit mostrera' una finestra di conferma nativa prima di applicare le modifiche.")

    pdf.h2("Template di prompt utili")
    templates = [
        ("Interrogare", "Elenca tutti i [CATEGORIA] al [LIVELLO] dove [PARAMETRO] = [VALORE]"),
        ("Contare", "Conta i [CATEGORIA] raggruppati per [PARAMETRO] e mostra come tabella"),
        ("Controllare", "Trova tutti i [CATEGORIA] dove [PARAMETRO] e' vuoto"),
        ("Modificare", "Imposta [PARAMETRO] = [VALORE] per tutti i [CATEGORIA] di tipo [TIPO] - mostrami prima"),
        ("Rinominare", "Rinomina tutte le viste [TIPO] usando il formato [FORMATO]"),
        ("Esportare", "Esporta i dati di tutti i [CATEGORIA] con [PARAMETRI] come CSV"),
        ("Duplicare tipo", "Duplica il tipo [TIPO] come [NUOVO_NOME] e imposta [PARAMETRO] a [VALORE]"),
        ("Creare tavole", "Crea le tavole per tutte le viste [TIPO], numerale [FORMATO]"),
        ("Confrontare", "Confronta i valori di [PARAMETRO] tra Piano 1 e Piano 2 per i [CATEGORIA]"),
    ]
    for label, template in templates:
        pdf.set_font("Helvetica", "B", 9)
        pdf.set_text_color(*C_ACCENT)
        pdf.cell(28, 5.5, label)
        pdf.set_font("Helvetica", "", 9)
        pdf.set_text_color(*C_DARK)
        pdf.cell(0, 5.5, template, new_x="LMARGIN", new_y="NEXT")

    # =======================================================
    # 03 - INTERROGARE IL MODELLO
    # =======================================================
    pdf.section_title("03", "Interrogare il modello")
    pdf.para("Questi strumenti leggono dati dal modello senza modificarlo. Sono sicuri da usare in qualsiasi momento.")

    pdf.tool_card("get_project_info",
        "Ottiene le informazioni generali del progetto: nome, autore, livelli, workset, fasi, file collegati.",
        "All'inizio di ogni sessione per capire con che modello stai lavorando.",
        [("Cos'e' questo progetto? Mostrami tutti i dettagli",
          "Nome: Edificio Residenziale A | Autore: Studio XYZ | 5 livelli | 3 workset | 2 link"),
         ("Mostrami solo i livelli con le loro quote",
          "Piano Terra: 0.00m | Piano 1: 3.50m | Piano 2: 7.00m | Copertura: 10.50m")],
        "La prima chiamata deve includere tutto. Le successive possono filtrare per risparmiare token.")

    pdf.tool_card("get_element_parameters",
        "Mostra tutti i parametri (istanza e tipo) di uno o piu' elementi, dato il loro ID.",
        "Quando vuoi ispezionare un elemento specifico o capire quali parametri ha disponibili.",
        [("Mostrami tutti i parametri dell'elemento 606873",
          "Lista completa: Nome tipo, Famiglia, Livello, Altezza, Larghezza, Resistenza al fuoco, Commenti..."),
         ("Quali sono i parametri di tipo di questa porta?",
          "Parametri tipo: [Type] Larghezza = 900mm | [Type] Altezza = 2100mm | [Type] Resistenza al fuoco = EI60")])

    pdf.tool_card("ai_element_filter",
        "Filtro intelligente per trovare elementi per categoria. Supporta filtri su tipi, istanze, e limiti.",
        "Quando vuoi una lista veloce di elementi di una categoria specifica.",
        [("Trova tutti i pilastri strutturali nel modello",
          "Trovati 47 pilastri strutturali: 12 tipo HEB240, 20 tipo HEB300, 15 tipo HEB360..."),
         ("Mostrami i primi 5 tipi di muro",
          "5 tipi: Muro di base 200mm | Muro di base 300mm | Muro Cortina | Muro Divisorio 100mm | Muro REI120")],
        "Avvolgi sempre i parametri nell'oggetto 'data': {\"data\": {\"filterCategory\": \"OST_Walls\"}}")

    pdf.tool_card("filter_by_parameter_value",
        "Cerca elementi filtrando per valore di un parametro specifico. Supporta: uguale, contiene, maggiore di, vuoto, ecc.",
        "Quando cerchi elementi con condizioni precise su un parametro.",
        [("Trova tutte le porte dove la resistenza al fuoco e' vuota",
          "18 porte senza resistenza al fuoco: D-101, D-105, D-112... (IDs: 12345, 12389, 12456)"),
         ("Quali muri al Piano 1 hanno larghezza maggiore di 200mm?",
          "32 muri trovati, tutti di tipo 'Muro di base 300mm' o 'Muro REI120'"),
         ("Trova le stanze senza numero assegnato",
          "5 stanze senza numero: Vano 1 (ID: 789), Vano 2 (ID: 801)...")],
        "Per parametri di tipo, usa sempre parameterType: 'type'. Il default 'both' puo' non risolvere correttamente.")

    pdf.tool_card("get_current_view_elements",
        "Elenca gli elementi visibili nella vista attiva. Puoi filtrare per categoria e limitare il numero.",
        "Quando vuoi analizzare solo cio' che si vede nella vista corrente.",
        [("Quante porte ci sono in questa vista?",
          "42 porte visibili nella vista 'Piano Terra - Architettonico'"),
         ("Elenca tutti i muri visibili con tipo e lunghezza",
          "Tabella: ID | Tipo | Lunghezza | 68 muri totali, lunghezza complessiva 1.247m")])

    pdf.tool_card("get_selected_elements",
        "Legge gli elementi attualmente selezionati in Revit.",
        "Quando hai selezionato elementi in Revit e vuoi lavorarci da Claude.",
        [("Cosa ho selezionato?",
          "3 elementi selezionati: Muro 12345 (Basic Wall 200mm), Porta 12389 (M_Single-Flush 900x2100), Finestra 12456"),
         ("Mostrami i parametri degli elementi selezionati",
          "Dettaglio completo per ciascun elemento con tutti i parametri istanza e tipo")])

    pdf.tool_card("export_elements_data",
        "Esporta dati di elementi come tabella JSON o CSV. Puoi scegliere quali categorie, parametri, e applicare filtri.",
        "Quando devi creare un report o esportare dati per analisi esterna.",
        [("Esporta tutti i dati delle porte come CSV con tipo, livello e resistenza al fuoco",
          "CSV generato con 156 righe: ID, Tipo, Livello, Resistenza al fuoco"),
         ("Dammi una tabella dei muri con nome tipo e lunghezza",
          "Tabella formattata: Tipo | Conteggio | Lunghezza totale | 12 tipi, 347 muri")],
        "Specifica sempre parameterNames e maxElements per limitare la risposta.")

    pdf.tool_card("export_room_data",
        "Esporta i dati di tutte le stanze: nome, numero, area, finiture, dipartimento.",
        "Per report sugli spazi, verifica delle aree, o esportazione per il brief di progetto.",
        [("Esporta i dati di tutte le stanze con aree e dipartimenti",
          "85 stanze esportate: Ufficio 101 (25.3 mq, Dipartimento A) | Corridoio 102 (12.1 mq)..."),
         ("Ci sono stanze non posizionate o non chiuse?",
          "3 stanze non posizionate, 2 stanze non chiuse (area = 0)")])

    pdf.tool_card("export_to_excel",
        "Esporta dati di elementi direttamente in un file Excel (.xlsx).",
        "Quando serve un file Excel da condividere con il team o i consulenti.",
        [("Esporta tutte le porte in un file Excel sul desktop",
          "File salvato: C:/Users/.../Desktop/Porte.xlsx - 156 righe, 12 colonne"),
         ("Crea un Excel con i dati delle stanze per il cliente",
          "File generato con foglio 'Stanze': Nome, Numero, Area, Dipartimento, Finitura pavimento")])

    pdf.tool_card("get_linked_elements",
        "Interroga gli elementi dentro i file Revit collegati (link).",
        "Quando devi verificare dati nei modelli collegati senza aprirli.",
        [("Mostra tutti i muri dal collegamento strutturale",
          "234 muri trovati nel link 'Strutture.rvt': 120 tipo CLS300, 89 tipo CLS200..."),
         ("Quante porte ci sono nel modello architettonico collegato?",
          "312 porte nel link 'Architettura.rvt', distribuite su 5 livelli")])

    pdf.tool_card("get_elements_in_spatial_volume",
        "Trova quali elementi sono dentro una stanza o un volume personalizzato.",
        "Per inventari di stanza, verifica arredi, o analisi degli spazi.",
        [("Quali mobili ci sono nella stanza 101?",
          "8 elementi: 2 scrivanie, 4 sedie, 1 armadio, 1 lampada"),
         ("Elenca tutti gli elementi nella stanza del direttore",
          "15 elementi trovati per categoria: Arredi (6), Impianti (4), Illuminazione (5)")])

    pdf.tool_card("get_room_openings",
        "Trova le porte e finestre associate a ciascuna stanza.",
        "Per report sulle aperture, verifica delle vie di fuga, o schede tecniche.",
        [("Quali porte e finestre ha la stanza 305?",
          "2 porte (M_Single-Flush 900x2100, M_Double-Flush 1800x2100) e 3 finestre (Fixed 1200x1500)"),
         ("Mostra le aperture di tutte le stanze al Piano 1",
          "Tabella per stanza: Stanza | Porte | Finestre | 25 stanze analizzate")])

    pdf.tool_card("find_untagged_elements",
        "Trova elementi che non hanno un tag nella vista corrente.",
        "Per il controllo qualita' della documentazione.",
        [("Quali porte non sono taggate in questa vista?",
          "12 porte senza tag: D-101, D-102, D-105... (posizionate ma non annotate)"),
         ("Trova le stanze senza tag",
          "3 stanze non taggate al Piano Terra")])

    pdf.tool_card("find_undimensioned_elements",
        "Trova elementi senza quote nella vista attiva.",
        "Per completare la documentazione e verificare che tutto sia quotato.",
        [("Quali muri non sono quotati in questa pianta?",
          "8 muri senza quote: IDs 456, 789, 1023... nella vista 'Piano Terra'")])

    pdf.tool_card("measure_between_elements",
        "Misura la distanza tra due elementi o due punti.",
        "Per verifiche rapide di distanze senza usare lo strumento quota di Revit.",
        [("Quanto distano il pilastro A1 dal pilastro B1?",
          "Distanza centro-centro: 6.00m | Distanza minima: 5.76m"),
         ("Misura la distanza tra questi due muri",
          "Distanza bounding box: 3.45m")])

    # =======================================================
    # 04 - CREARE ELEMENTI
    # =======================================================
    pdf.section_title("04", "Creare elementi")
    pdf.para("Questi strumenti creano nuovi elementi nel modello. Revit mostrera' sempre una finestra di conferma "
             "prima di eseguire operazioni importanti.")

    pdf.tool_card("create_point_based_element",
        "Posiziona elementi basati su punto: porte, finestre, pilastri, arredi, apparecchi.",
        "Quando devi piazzare uno o piu' elementi in posizioni specifiche.",
        [("Posiziona una scrivania alle coordinate (5, 3, 0) al Piano 1",
          "Scrivania creata: ID 67890, famiglia 'Desk 1500x800', posizione (5.0, 3.0, 0.0)"),
         ("Metti un pilastro ad ogni intersezione della griglia",
          "12 pilastri creati alle intersezioni A1-A4, B1-B4, C1-C4")])

    pdf.tool_card("create_line_based_element",
        "Crea elementi basati su linea: muri, travi, tubi, cavi.",
        "Per creare muri, travi o altri elementi lineari definiti da punto iniziale e finale.",
        [("Crea un muro dal punto (0,0) al punto (10,0) al Piano Terra",
          "Muro creato: ID 67891, tipo 'Basic Wall 200mm', lunghezza 10.0m"),
         ("Aggiungi una trave HEB300 da (0,0,3.5) a (8,0,3.5)",
          "Trave creata: ID 67892, tipo HEB300, campata 8.0m")])

    pdf.tool_card("create_floor",
        "Crea un solaio da un contorno di punti o dal perimetro di una stanza.",
        "Per aggiungere pavimenti a stanze esistenti o creare solai personalizzati.",
        [("Crea un pavimento in cemento nella stanza 205",
          "Pavimento creato: ID 67893, tipo 'Concrete 150mm', area 25.3 mq"),
         ("Aggiungi un solaio al Piano 2 con questi 4 punti angolo",
          "Solaio creato con contorno personalizzato, area 120 mq")])

    pdf.tool_card("create_room",
        "Crea un elemento stanza (vano) in una posizione specifica.",
        "Quando mancano stanze nel modello o devi aggiungerne di nuove.",
        [("Crea una stanza 'Ufficio' al Piano 1 alle coordinate (5, 8)",
          "Stanza creata: 'Ufficio', numero auto-assegnato, area 18.5 mq"),
         ("Aggiungi il vano 'Archivio' con numero 201 al Piano 2",
          "Vano 201 'Archivio' creato al Piano 2")])

    pdf.tool_card("create_grid",
        "Crea un sistema di griglie (assi) con spaziatura regolare.",
        "All'inizio di un progetto o quando serve aggiungere una griglia strutturale.",
        [("Crea una griglia 5x4 con passo 6 metri, assi A-E e 1-4",
          "Griglia creata: 5 assi X (A-E) + 4 assi Y (1-4), passo 6.0m"),
         ("Aggiungi 3 griglie orizzontali con passo 8 metri partendo da 1",
          "3 griglie create: 1, 2, 3 con spaziatura 8.0m")])

    pdf.tool_card("create_level",
        "Crea un nuovo livello a una quota specifica.",
        "Per aggiungere piani, mezzanini, o livelli di copertura.",
        [("Crea il Piano 3 a quota 10.5 metri",
          "Livello 'Piano 3' creato a +10.500m, pianta creata automaticamente"),
         ("Aggiungi un mezzanino a quota 4.5m senza creare piante",
          "Livello 'Mezzanino' creato a +4.500m, nessuna vista associata")])

    pdf.tool_card("create_array",
        "Crea serie lineari o radiali di elementi esistenti.",
        "Per replicare elementi con spaziatura regolare.",
        [("Copia questo pilastro 5 volte con passo 3 metri in direzione X",
          "5 copie create: spaziatura 3.0m, IDs: 67901-67905"),
         ("Crea una serie radiale di 8 elementi attorno al centro (5,5)",
          "8 copie create a 45 gradi di distanza, raggio 3.0m")])

    pdf.tool_card("create_structural_framing_system",
        "Crea una griglia di travi su un livello specificato.",
        "Per posizionare rapidamente un sistema di travi regolare.",
        [("Crea un travettato al Piano 2 con passo 1.2m tra gli assi A-D",
          "12 travi create: tipo IPE240, campata 6.0m, passo 1.2m")])

    pdf.tool_card("copy_elements",
        "Copia elementi con un offset in X, Y, Z.",
        "Per duplicare gruppi di elementi con uno spostamento.",
        [("Copia questi elementi 3 metri a destra",
          "5 elementi copiati con offset X=3.0m, nuovi IDs: 68001-68005"),
         ("Duplica questo gruppo al piano superiore",
          "Elementi copiati con offset Z=3.5m")])

    pdf.tool_card("create_filled_region",
        "Crea una regione campita in una vista.",
        "Per evidenziare zone nelle piante o creare aree grafiche.",
        [("Disegna una regione campita intorno a questa zona",
          "Regione creata con campittura 'Solid Fill', area 45 mq")])

    pdf.tool_card("create_surface_based_element",
        "Crea elementi basati su superfici come tetti, pavimenti o controsoffitti definendo i punti perimetrali.",
        "Per creare elementi con geometria personalizzata da coordinate.",
        [("Crea un tetto seguendo questi punti di perimetro",
          "Tetto creato: tipo 'Generic - 400mm', area 125 mq, 4 pendenze"),
         ("Crea un pavimento con forma personalizzata in questa zona",
          "Pavimento creato: tipo 'Concrete Slab 200mm', area 68 mq")])

    # =======================================================
    # 05 - MODIFICARE ELEMENTI
    # =======================================================
    pdf.section_title("05", "Modificare elementi")
    pdf.para("Tutte le modifiche mostrano una finestra di conferma in Revit. Puoi sempre annullare con Ctrl+Z.")
    pdf.warn("Le modifiche in massa sono potenti. Usa sempre dryRun o chiedi un'anteprima prima di applicare.")

    pdf.tool_card("set_element_parameters",
        "Imposta il valore di uno o piu' parametri su uno o piu' elementi.",
        "Per modificare qualsiasi parametro modificabile: commenti, contrassegno, fase, ecc.",
        [("Imposta il Contrassegno della porta 12345 a 'D-001'",
          "Parametro aggiornato: porta 12345, Mark = 'D-001'"),
         ("Cambia i Commenti a 'Verificato' per gli elementi 100, 200, 300",
          "3 elementi aggiornati: Comments = 'Verificato'"),
         ("Imposta la resistenza al fuoco 'EI60' per tutte le porte selezionate",
          "Conferma Revit -> 8 porte aggiornate")])

    pdf.tool_card("modify_element",
        "Sposta, ruota, specchia o copia elementi nel modello.",
        "Per riposizionare elementi senza selezionarli manualmente in Revit.",
        [("Sposta il muro 5678 di 2 metri in direzione X",
          "Muro spostato: traslazione X=2.0m, nuova posizione confermata"),
         ("Ruota questo elemento di 45 gradi attorno al suo centro",
          "Rotazione applicata: 45 gradi in senso antiorario"),
         ("Specchia questi 3 elementi rispetto all'asse Y",
          "3 elementi specchiati con successo")])

    pdf.tool_card("change_element_type",
        "Cambia il tipo di uno o piu' elementi.",
        "Quando devi aggiornare elementi a un tipo diverso della stessa famiglia.",
        [("Cambia tutti i muri 'Basic Wall 200mm' in 'Basic Wall 300mm'",
          "Conferma Revit -> 45 muri aggiornati al nuovo tipo"),
         ("Sostituisci queste porte con il tipo 'Fire Door EI60'",
          "12 porte cambiate al tipo 'Fire Door EI60'")])

    pdf.tool_card("operate_element",
        "Seleziona elementi in Revit, oppure applica override grafici rapidi.",
        "Per evidenziare elementi problematici o selezionarli per lavorarci.",
        [("Seleziona gli elementi 123, 456 e 789 in Revit",
          "3 elementi selezionati nella vista attiva"),
         ("Evidenzia i muri problematici in rosso",
          "Override grafico applicato: colore rosso, 15 elementi")])

    pdf.tool_card("color_elements",
        "Colora gli elementi per categoria in base al valore di un parametro.",
        "Per visualizzazione tematica: stanze per dipartimento, muri per tipo, ecc.",
        [("Colora tutti i muri per tipo",
          "Vista colorata: 5 colori assegnati a 5 tipi diversi, legenda creata"),
         ("Mostra le stanze colorate per dipartimento",
          "Stanze colorate: Marketing=blu, IT=verde, HR=arancione...")])

    pdf.tool_card("match_element_properties",
        "Copia i valori dei parametri da un elemento sorgente a uno o piu' elementi target.",
        "Per applicare le stesse proprieta' a elementi simili senza riscrivere tutto.",
        [("Copia il Contrassegno e i Commenti dall'elemento A agli elementi B, C e D",
          "2 parametri copiati su 3 elementi: Mark, Comments"),
         ("Applica le proprieta' della porta 123 a tutte le altre porte di questo tipo",
          "8 parametri trasferiti a 24 porte")])

    pdf.tool_card("delete_element",
        "Elimina elementi dal modello per ID.",
        "Quando devi rimuovere elementi specifici.",
        [("Elimina gli elementi 123, 456 e 789",
          "Conferma Revit: 'Eliminare 3 elementi?' -> 3 elementi eliminati"),
         ("Rimuovi le griglie non utilizzate",
          "dryRun: 4 griglie sarebbero eliminate. Procedi? -> 4 griglie rimosse")],
        warns="Operazione irreversibile (oltre Ctrl+Z). Usa sempre dryRun prima.")

    pdf.tool_card("save_selection",
        "Salva la selezione corrente come set con un nome.",
        "Per salvare un gruppo di elementi selezionati e riutilizzarli in seguito.",
        [("Salva questa selezione come 'Muri da verificare'",
          "Selezione 'Muri da verificare' salvata con 18 elementi")])

    pdf.tool_card("load_selection",
        "Carica una selezione salvata precedentemente e la seleziona nella vista.",
        "Per recuperare un set di elementi salvato e continuare a lavorarci.",
        [("Carica la selezione 'Muri da verificare'",
          "18 elementi selezionati dalla selezione 'Muri da verificare'")])

    pdf.tool_card("delete_selection",
        "Elimina un set di selezione salvato.",
        "Per fare pulizia dei set di selezione non piu' necessari.",
        [("Elimina la selezione salvata 'Muri da verificare'",
          "Set di selezione 'Muri da verificare' eliminato")])

    pdf.tool_card("set_element_phase",
        "Imposta la fase di creazione o demolizione degli elementi.",
        "Per gestire le fasi di progetto (esistente, nuova costruzione, demolizione).",
        [("Imposta la fase 'Nuova Costruzione' per questi muri",
          "12 muri aggiornati: Fase di creazione = 'Nuova Costruzione'"),
         ("Segna questi elementi come demoliti nella fase 2",
          "8 elementi impostati come demoliti")])

    pdf.tool_card("set_element_workset",
        "Sposta elementi in un workset diverso.",
        "Per organizzare gli elementi nei workset corretti in modelli condivisi.",
        [("Sposta tutte le porte nel workset 'Architettura - Porte'",
          "156 porte spostate nel workset 'Architettura - Porte'"),
         ("Cambia il workset degli elementi selezionati",
          "23 elementi spostati nel workset specificato")])

    pdf.tool_card("override_graphics",
        "Applica override grafici (colori, trasparenza, spessore linea) a elementi specifici.",
        "Per evidenziare elementi in una vista senza modificarne le proprieta'.",
        [("Colora questi muri di rosso con 50% di trasparenza",
          "Override applicato: rosso (255,0,0), trasparenza 50%, 8 elementi"),
         ("Ripristina gli override grafici per l'elemento 123",
          "Override rimossi per l'elemento 123")])

    pdf.tool_card("send_code_to_revit",
        "ULTIMA RISORSA: esegue codice C# personalizzato dentro Revit. Disabilitato di default - abilitalo da Settings > Tools (richiede conferma esplicita a ogni esecuzione). Il codice e' validato da un sandbox di sicurezza.",
        "SOLO quando nessuno strumento dedicato copre l'operazione. Per parametri, filtri, rinomine, statistiche e viste usa sempre gli strumenti dedicati.",
        [("Crea una geometria DirectShape non coperta dagli strumenti standard",
          "Codice eseguito dopo conferma. Geometria creata.")],
        warns="Disabilitato di default (EnableCodeExecution in settings.json). Il codice non puo' accedere al filesystem, rete, registro o processi. Namespace vietati: System.IO, System.Net, System.Diagnostics.Process.")

    # =======================================================
    # 06 - TIPI E FAMIGLIE
    # =======================================================
    pdf.section_title("06", "Tipi e famiglie")
    pdf.para("Gestisci i tipi di famiglia: duplica per creare varianti, carica nuove famiglie, elenca i tipi disponibili.")

    pdf.tool_card("duplicate_family_type",
        "Duplica un tipo di famiglia caricabile (porta, finestra, arredo, ecc.) con un nuovo nome. "
        "Puoi anche modificare i parametri del nuovo tipo nella stessa operazione.",
        "Quando vuoi creare una variante di un tipo esistente - per esempio, una porta piu' stretta o una finestra piu' alta.",
        [("Duplica la porta '900x2100' come '800x2100' e imposta la larghezza a 800",
          "Tipo creato: '800x2100' nella famiglia M_Single-Flush, Width=800mm impostato"),
         ("Crea una variante della finestra 'Fixed 1200x1500' chiamata 'Fixed 1500x1500' con altezza 1500",
          "Tipo duplicato: 'Fixed 1500x1500', parametro Height=1500mm applicato"),
         ("Duplica il tipo 'Desk 1500x800' come 'Desk 1200x600'",
          "Nuovo tipo creato nella famiglia Desk. Usa set_element_parameters per cambiare le dimensioni.")],
        "Se lo stesso nome tipo esiste in piu' famiglie, specifica familyName per disambiguare.")

    pdf.tool_card("duplicate_system_type",
        "Duplica un tipo di famiglia di sistema (muro, pavimento, tetto, controsoffitto).",
        "Per creare varianti di tipi di sistema senza partire da zero.",
        [("Duplica il tipo muro 'Basic Wall 200mm' come 'Basic Wall 250mm'",
          "Tipo creato: 'Basic Wall 250mm', categoria Muri"),
         ("Crea una copia del tipo pavimento 'Concrete 150mm'",
          "Tipo 'Concrete 150mm - Copia' creato")])

    pdf.tool_card("load_family",
        "Carica una famiglia .rfa nel modello, oppure elenca le famiglie disponibili per categoria.",
        "Per importare famiglie da file esterni o esplorare il catalogo del modello.",
        [("Carica la famiglia dal file C:/Famiglie/PortaCustom.rfa",
          "Famiglia 'PortaCustom' caricata con 3 tipi"),
         ("Elenca le famiglie disponibili nella categoria Porte",
          "8 famiglie: M_Single-Flush (4 tipi), M_Double-Flush (2 tipi)...")])

    pdf.tool_card("get_available_family_types",
        "Elenca tutti i tipi di famiglia disponibili, con filtro per categoria o nome famiglia.",
        "Per sapere quali tipi puoi usare prima di creare o cambiare elementi.",
        [("Mostra tutti i tipi di porta disponibili",
          "24 tipi in 5 famiglie: M_Single-Flush (900x2100, 800x2100, 700x2100)..."),
         ("Quali tipi ha la famiglia 'Fixed Window'?",
          "3 tipi: 1200x1500, 1500x1500, 900x1200")])

    pdf.tool_card("export_families",
        "Esporta le famiglie dal modello come file .rfa su disco.",
        "Per estrarre famiglie dal modello per riutilizzarle in altri progetti.",
        [("Esporta tutte le famiglie di porte nella cartella C:/Export",
          "12 famiglie esportate in C:/Export/Porte/: M_Single-Flush.rfa, M_Double-Flush.rfa..."),
         ("Esporta le famiglie di arredo raggruppate per categoria",
          "8 famiglie esportate in sottocartelle per categoria")])

    # =======================================================
    # 07 - VISTE E TAVOLE
    # =======================================================
    pdf.section_title("07", "Viste e tavole")

    pdf.tool_card("create_view",
        "Crea piante, piante controsoffitto, sezioni, o viste 3D.",
        "Quando ti serve una nuova vista del modello.",
        [("Crea una pianta del Piano 2 in scala 1:100",
          "Vista 'Piano 2' creata, scala 1:100, ID: 78901"),
         ("Crea una sezione guardando verso nord",
          "Sezione creata: direzione Nord, profondita' 20m"),
         ("Crea una vista 3D del modello",
          "Vista 3D creata con nome auto-generato")])

    pdf.tool_card("duplicate_view",
        "Duplica viste con opzioni: copia semplice, con detailing, o come dipendente.",
        "Per creare copie di lavoro delle viste esistenti.",
        [("Duplica questa vista con tutti i dettagli",
          "Vista duplicata con dettagli: 'Piano Terra - Copia'"),
         ("Crea 3 viste dipendenti dalla pianta del Piano 1",
          "3 viste dipendenti create")])

    pdf.tool_card("create_view_filter",
        "Crea filtri di vista e applicali con override grafici.",
        "Per evidenziare o nascondere elementi in base a regole.",
        [("Crea un filtro che mostra i muri REI in rosso",
          "Filtro 'Muri REI' creato, applicato alla vista con colore rosso"),
         ("Nascondi gli elementi demoliti nella vista corrente",
          "Filtro 'Demoliti' applicato, elementi nascosti")])

    pdf.tool_card("apply_view_template",
        "Applica o rimuovi template di vista.",
        "Per standardizzare l'aspetto delle viste.",
        [("Applica il template 'Pianta Strutturale' a tutte le piante",
          "Template applicato a 8 viste"),
         ("Rimuovi il template dalle viste selezionate",
          "Template rimosso da 3 viste")])

    pdf.tool_card("rename_views",
        "Rinomina viste in massa con prefissi, suffissi, o trova-e-sostituisci.",
        "Per standardizzare i nomi delle viste.",
        [("Aggiungi il prefisso 'REV-' a tutte le sezioni",
          "12 sezioni rinominate: 'Section 1' -> 'REV-Section 1'..."),
         ("Sostituisci 'OLD' con 'NEW' nei nomi delle viste",
          "5 viste rinominate")])

    pdf.tool_card("create_sheet",
        "Crea una singola tavola con cartiglio.",
        "Per aggiungere tavole al set documentale.",
        [("Crea la tavola A-101 'Pianta Piano Terra'",
          "Tavola A-101 creata con cartiglio standard")])

    pdf.tool_card("batch_create_sheets",
        "Crea piu' tavole contemporaneamente.",
        "Per impostare rapidamente il set di tavole di un progetto.",
        [("Crea le tavole da A-101 ad A-110 con il nostro cartiglio aziendale",
          "10 tavole create: A-101...A-110 con cartiglio 'Company Titleblock'")])

    pdf.tool_card("place_viewport",
        "Posiziona una vista su una tavola.",
        "Per comporre le tavole con le viste desiderate.",
        [("Metti la pianta del Piano 1 sulla tavola A-101",
          "Viewport posizionato al centro della tavola A-101")])

    pdf.tool_card("align_viewports",
        "Allinea i viewport tra tavole diverse.",
        "Per mantenere la coerenza grafica tra tavole.",
        [("Allinea tutti i viewport alla posizione del primo",
          "5 viewport allineati alla posizione di riferimento")])

    pdf.tool_card("duplicate_sheet_with_content",
        "Duplica una tavola con tutto il suo contenuto (viste, legende, abachi).",
        "Per creare copie di tavole complete.",
        [("Fai 3 copie della tavola A-101 con tutte le viste",
          "3 tavole duplicate: A-101a, A-101b, A-101c con viste copiate")])

    pdf.tool_card("duplicate_sheet_with_views",
        "Duplica tavola con opzioni avanzate per le viste (duplica, con detailing, dipendente).",
        "Quando vuoi controllare come vengono duplicate le viste nella copia.",
        [("Duplica la tavola con viste dipendenti",
          "Tavola duplicata, 3 viste create come dipendenti")])

    pdf.tool_card("create_views_from_rooms",
        "Crea automaticamente viste (callout, sezioni, prospetti) centrate sulle stanze.",
        "Per documentazione automatica degli spazi interni.",
        [("Crea sezioni per tutte le stanze del Piano 1",
          "25 sezioni create, una per stanza, nome 'Section - Ufficio 101'..."),
         ("Genera callout per le stanze 101-110",
          "10 callout creati nelle piante corrispondenti")])

    pdf.tool_card("create_placeholder_sheets",
        "Crea tavole segnaposto per la pianificazione del set documentale.",
        "Per pianificare il set di tavole prima di avere le viste pronte.",
        [("Crea tavole segnaposto per il set documentale",
          "15 segnaposto creati: S-001...S-015")])

    pdf.tool_card("manage_view_templates",
        "Elenca, duplica, elimina o rinomina template di vista.",
        "Per gestire la libreria di template.",
        [("Elenca tutti i template di vista",
          "12 template: Pianta Arch., Pianta Strutturale, Sezione, 3D...")])

    pdf.tool_card("manage_unplaced_views",
        "Trova o elimina viste non posizionate su nessuna tavola.",
        "Per la pulizia del modello, eliminando viste orfane.",
        [("Mostrami tutte le viste non posizionate su tavole",
          "34 viste non posizionate: 12 piante, 8 sezioni, 14 3D"),
         ("Elimina le piante non posizionate (anteprima prima)",
          "dryRun: 12 piante verrebbero eliminate. Confermi?")])

    pdf.tool_card("batch_modify_view_range",
        "Modifica il range di vista (piano di taglio, top, bottom) per piu' viste.",
        "Per standardizzare il range di vista su piu' piante.",
        [("Imposta il piano di taglio a 1200mm per tutte le piante",
          "8 piante aggiornate: piano di taglio = 1200mm")])

    pdf.tool_card("section_box_from_selection",
        "Crea una vista 3D con section box attorno agli elementi selezionati.",
        "Per isolare e visualizzare elementi specifici in 3D.",
        [("Crea una vista 3D ritagliata attorno a queste travi",
          "Vista 3D 'Section Box - Travi' creata con box automatico + offset 0.5m")])

    pdf.tool_card("get_current_view_info",
        "Mostra informazioni sulla vista attiva in Revit.",
        "Per sapere in che vista ti trovi prima di operare.",
        [("In che vista sono?",
          "Vista attiva: 'Piano Terra - Architettonico', tipo FloorPlan, scala 1:100")])

    pdf.tool_card("batch_export",
        "Esporta viste e tavole in formati CAD (DWG, DXF, DGN) o immagini.",
        "Per consegnare tavole in formato CAD o generare immagini per presentazioni.",
        [("Esporta tutte le tavole in DWG",
          "24 tavole esportate in DWG nella cartella di output"),
         ("Esporta le viste del Piano Terra come immagini PNG",
          "3 viste esportate: Piano Terra - Architettonico.png, Piano Terra - Strutturale.png...")])

    # =======================================================
    # 08 - ABACHI E SCHEDULE
    # =======================================================
    pdf.section_title("08", "Abachi e schedule")

    pdf.tool_card("create_preset_schedule",
        "Crea un abaco da un modello predefinito: porte per stanza, finestre, stanze, materiali, elenco tavole.",
        "Il modo piu' veloce per creare abachi standard.",
        [("Crea un abaco porte per stanza",
          "Abaco 'Porte per Stanza' creato con campi: Stanza, Numero Porta, Tipo, Dimensioni"),
         ("Crea un elenco tavole",
          "Elenco tavole creato con Numero, Nome, Revisione")])

    pdf.tool_card("create_schedule",
        "Crea un abaco personalizzato scegliendo categoria, tipo e campi.",
        "Quando i preset non bastano e vuoi un abaco su misura.",
        [("Crea un abaco porte con Nome, Contrassegno e Resistenza al Fuoco",
          "Abaco 'Porte' creato con 3 campi, ordinato per Nome")])

    pdf.tool_card("get_schedule_data",
        "Legge i dati di un abaco esistente come tabella.",
        "Per consultare i dati di un abaco senza aprirlo in Revit.",
        [("Mostrami i dati dell'abaco 'Abaco Porte'",
          "156 righe: Porta D-001 | M_Single-Flush 900x2100 | EI60 | Piano 1..."),
         ("Leggi le prime 20 righe dell'elenco stanze",
          "20 righe con Nome, Numero, Area, Dipartimento")])

    pdf.tool_card("export_schedule",
        "Esporta un abaco come file CSV/TSV.",
        "Per condividere i dati con team esterni o importarli in Excel.",
        [("Esporta l'abaco porte come CSV",
          "File salvato: DoorSchedule.csv, 156 righe esportate")])

    pdf.tool_card("modify_schedule",
        "Aggiungi/rimuovi campi, imposta l'ordinamento, rinomina un abaco.",
        "Per modificare la struttura di un abaco esistente.",
        [("Aggiungi il campo 'Resistenza al Fuoco' all'abaco porte",
          "Campo aggiunto in ultima posizione"),
         ("Ordina l'abaco stanze per Livello e poi per Nome",
          "Ordinamento applicato: 1) Livello, 2) Nome")])

    pdf.tool_card("duplicate_schedule",
        "Duplica un abaco esistente con un nuovo nome.",
        "Per creare varianti di abachi senza ripartire da zero.",
        [("Duplica 'Abaco Porte' come 'Abaco Porte - QC'",
          "Abaco duplicato: 'Abaco Porte - QC' con stessi campi e filtri")])

    pdf.tool_card("delete_schedule",
        "Elimina un abaco dal modello.",
        "Per rimuovere abachi obsoleti o duplicati.",
        [("Elimina l'abaco 'Vecchio Abaco Porte'",
          "Conferma Revit -> Abaco eliminato")])

    pdf.tool_card("create_revision",
        "Crea una revisione o aggiunge revisioni alle tavole.",
        "Per gestire il ciclo di revisioni del progetto.",
        [("Crea una nuova revisione con data odierna e descrizione 'Revisione Cliente'",
          "Revisione #3 creata: data 2026-04-13, 'Revisione Cliente'"),
         ("Aggiungi la revisione 1 alle tavole A-101 fino ad A-110",
          "Revisione aggiunta a 10 tavole")])

    pdf.tool_card("list_schedulable_fields",
        "Mostra quali campi puoi aggiungere a un abaco per una data categoria.",
        "Prima di creare un abaco, per sapere quali parametri sono disponibili.",
        [("Quali campi posso usare in un abaco porte?",
          "42 campi disponibili: Family and Type, Width, Height, Fire Rating, Level, Room...")])

    # =======================================================
    # 09 - ANNOTAZIONI E TAG
    # =======================================================
    pdf.section_title("09", "Annotazioni e tag")

    pdf.tool_card("tag_rooms",
        "Aggiunge tag a tutte le stanze nella vista corrente.",
        "Per annotare rapidamente le stanze dopo averle create o verificate.",
        [("Tagga tutte le stanze in questa vista",
          "25 tag aggiunti nella vista 'Piano Terra'"),
         ("Aggiungi tag con leader alle stanze 101-110",
          "10 tag con leader posizionati")])

    pdf.tool_card("tag_walls",
        "Aggiunge tag ai muri nella vista corrente.",
        "Per annotare i muri con il loro tipo nelle piante.",
        [("Tagga tutti i muri in questa vista",
          "68 tag muro aggiunti nella pianta")])

    pdf.tool_card("create_dimensions",
        "Crea linee di quota tra elementi.",
        "Per quotare distanze tra griglie, muri, o altri elementi.",
        [("Quota la distanza tra la griglia A e la griglia B",
          "Quota creata: 6.00m tra Grid A e Grid B")])

    pdf.tool_card("create_text_note",
        "Crea note di testo nelle viste.",
        "Per aggiungere annotazioni e commenti nelle viste.",
        [("Aggiungi una nota 'Verificare in cantiere' in questa posizione",
          "Nota di testo creata nella vista attiva")])

    pdf.tool_card("create_color_legend",
        "Crea una legenda colori per i valori di un parametro.",
        "Per accompagnare le viste colorate con una legenda leggibile.",
        [("Crea una legenda per le stanze colorate per dipartimento",
          "Legenda creata con 6 colori: Marketing=blu, IT=verde, HR=arancione...")])

    pdf.tool_card("import_table",
        "Importa una tabella CSV/TSV in una vista disegno o legenda.",
        "Per inserire tabelle esterne nella documentazione Revit.",
        [("Importa la tabella dal file CSV nella vista disegno",
          "Tabella importata: 25 righe x 5 colonne nella vista 'Dettagli'")])

    pdf.tool_card("wipe_empty_tags",
        "Rimuove tutti i tag con valore vuoto dalla vista corrente.",
        "Per pulire la documentazione da tag non compilati.",
        [("Elimina i tag stanza vuoti nella vista corrente",
          "dryRun: 8 tag vuoti trovati. Procedi? -> 8 tag rimossi"),
         ("Pulisci tutti i tag vuoti delle porte",
          "12 tag porte vuoti rimossi")])

    # =======================================================
    # 10 - MATERIALI E STRATIGRAFIE
    # =======================================================
    pdf.section_title("10", "Materiali e stratigrafie")

    pdf.tool_card("get_materials",
        "Elenca i materiali nel modello, con filtro per classe o nome.",
        "Per esplorare la libreria materiali del modello.",
        [("Mostra tutti i materiali del modello",
          "48 materiali: Calcestruzzo (5), Acciaio (3), Vetro (2), Laterizio (4)..."),
         ("Trova i materiali con 'Cemento' nel nome",
          "3 materiali: Cemento C25/30, Cemento Alleggerito, Cemento Armato")])

    pdf.tool_card("get_material_properties",
        "Mostra le proprieta' dettagliate di un materiale.",
        "Per ispezionare colore, trasparenza, classe e asset di un materiale.",
        [("Mostra le proprieta' del materiale 'Calcestruzzo - Getto in Opera'",
          "Classe: Calcestruzzo | Colore: grigio (180,180,180) | Trasparenza: 0% | Asset strutturale: presente")])

    pdf.tool_card("get_material_quantities",
        "Calcola le quantita' di materiali usati per categoria.",
        "Per computi metrici e analisi dei materiali.",
        [("Quanti materiali sono usati nei muri e nei pavimenti?",
          "Muri: Cemento 450 mq, Laterizio 320 mq | Pavimenti: Gres 280 mq, Cemento 150 mq")])

    pdf.tool_card("create_material",
        "Crea un nuovo materiale con colore, trasparenza e classe.",
        "Per aggiungere materiali personalizzati al modello.",
        [("Crea un materiale 'Vetro Blu' con trasparenza 40% e colore blu",
          "Materiale 'Vetro Blu' creato: colore (0,100,200), trasparenza 40%")])

    pdf.tool_card("duplicate_material",
        "Duplica un materiale esistente con tutte le sue proprieta'.",
        "Per creare varianti di materiali esistenti.",
        [("Duplica 'Calcestruzzo' come 'Calcestruzzo Speciale'",
          "Materiale duplicato con tutti gli asset (aspetto, strutturale, termico)")])

    pdf.tool_card("delete_material",
        "Elimina un materiale dal modello.",
        "Per rimuovere materiali inutilizzati.",
        [("Elimina il materiale 'Test Material'",
          "Conferma Revit -> Materiale eliminato")])

    pdf.tool_card("set_material_properties",
        "Modifica le proprieta' di materiali esistenti.",
        "Per aggiornare colori, trasparenza o altre proprieta'.",
        [("Cambia il colore del calcestruzzo a grigio scuro",
          "Colore aggiornato: (100,100,100)"),
         ("Imposta la trasparenza del vetro al 50%",
          "Trasparenza aggiornata: 50%")])

    pdf.tool_card("get_compound_structure",
        "Mostra la stratigrafia (composizione a strati) di muri, pavimenti o tetti.",
        "Per verificare la composizione costruttiva di un tipo.",
        [("Mostra la stratigrafia del muro 'Muro Esterno 300mm'",
          "4 strati: Intonaco 15mm | Laterizio 120mm | Isolamento 80mm | Cartongesso 12.5mm"),
         ("Qual'e' la composizione del pavimento tipo?",
          "3 strati: Massetto 60mm | Cemento 150mm | Impermeabilizzazione 5mm")])

    pdf.tool_card("set_compound_structure",
        "Modifica la stratigrafia di muri, pavimenti o tetti. Puoi aggiungere, rimuovere o sostituire strati.",
        "Per cambiare lo spessore dell'isolamento, aggiungere una finitura, ecc.",
        [("Aggiungi uno strato di isolamento da 80mm al muro",
          "Strato aggiunto: Isolamento 80mm, posizione 3/5"),
         ("Sostituisci la finitura con cartongesso da 12.5mm",
          "Strato 1 sostituito: Cartongesso 12.5mm")])

    # =======================================================
    # 11 - FILE COLLEGATI
    # =======================================================
    pdf.section_title("11", "File collegati")

    pdf.tool_card("get_linked_file_instances",
        "Elenca tutti i file Revit collegati con il loro stato.",
        "Per avere una panoramica dei link nel modello.",
        [("Mostra tutti i file collegati",
          "3 link: Strutture.rvt (caricato), Impianti.rvt (caricato), Architettura_OLD.rvt (scaricato)")])

    pdf.tool_card("manage_links",
        "Elenca, ricarica o scarica i collegamenti.",
        "Per gestire lo stato dei link senza usare il dialog 'Gestisci Link' di Revit.",
        [("Ricarica tutti i collegamenti",
          "3 link ricaricati con successo"),
         ("Scarica il link 'Architettura_OLD.rvt'",
          "Link scaricato dalla memoria")])

    pdf.tool_card("add_linked_file",
        "Aggiunge un nuovo file Revit come collegamento.",
        "Per collegare un nuovo modello al progetto.",
        [("Collega il modello strutturale da questo percorso",
          "Link aggiunto: 'Strutture.rvt', posizione origine")])

    pdf.tool_card("reload_linked_file_from",
        "Ricarica un link da un percorso diverso.",
        "Quando il file collegato e' stato spostato o rinominato.",
        [("Ricarica il link strutturale dal nuovo percorso",
          "Link ricaricato dal nuovo percorso, 234 elementi aggiornati")])

    pdf.tool_card("get_link_transform",
        "Mostra posizione e rotazione di un'istanza di link.",
        "Per verificare l'allineamento dei link.",
        [("Dove si trova il collegamento strutturale?",
          "Posizione: (0.0, 0.0, 0.0) | Rotazione: 0 gradi | Sistema: Condiviso")])

    pdf.tool_card("align_link_to_host",
        "Allinea un link all'origine o alle coordinate condivise.",
        "Per riposizionare correttamente un link disallineato.",
        [("Allinea il link strutturale alle coordinate condivise",
          "Link allineato al sistema di coordinate condivise")])

    pdf.tool_card("move_link_instance",
        "Sposta un'istanza di link nel modello.",
        "Per riposizionare un link con precisione.",
        [("Sposta il link MEP di 5 metri verso est",
          "Link spostato: traslazione X=5.0m")])

    pdf.tool_card("pin_unpin_link_instance",
        "Blocca o sblocca le istanze di link.",
        "Per proteggere i link da spostamenti accidentali.",
        [("Blocca tutti i link",
          "3 istanze bloccate"),
         ("Sblocca il link strutturale per riposizionarlo",
          "1 istanza sbloccata")])

    pdf.tool_card("highlight_linked_element",
        "Evidenzia e zooma su un elemento specifico dentro un link, con section box opzionale.",
        "Per localizzare visivamente un elemento in un modello collegato.",
        [("Mostrami l'elemento 789 nel link strutturale",
          "Elemento evidenziato, section box creato con offset 2m")])

    pdf.tool_card("get_selected_linked_elements",
        "Legge le informazioni degli elementi selezionati che appartengono ai link.",
        "Quando selezioni elementi di un link in Revit e vuoi analizzarli.",
        [("Cosa ho selezionato nei link?",
          "2 elementi dal link 'Strutture.rvt': Trave HEB300 (ID: 45678), Pilastro HEB240 (ID: 45690)")])

    # =======================================================
    # 12 - RINOMINA E NUMERAZIONE
    # =======================================================
    pdf.section_title("12", "Rinomina e numerazione")

    pdf.tool_card("batch_rename",
        "Rinomina elementi in massa con trova-e-sostituisci, prefisso, o suffisso.",
        "Per standardizzare i nomi di viste, tavole, livelli, stanze.",
        [("Rinomina tutte le viste sostituendo 'OLD' con 'NEW'",
          "5 viste rinominate: 'OLD Piano 1' -> 'NEW Piano 1'..."),
         ("Aggiungi il prefisso 'QC-' a tutti i nomi delle stanze",
          "85 stanze rinominate: 'Ufficio' -> 'QC-Ufficio'...")])

    pdf.tool_card("rename_families",
        "Rinomina famiglie e opzionalmente i loro tipi.",
        "Per standardizzare i nomi delle famiglie nel modello.",
        [("Aggiungi il prefisso 'STD-' a tutte le famiglie porte",
          "8 famiglie rinominate: 'M_Single-Flush' -> 'STD-M_Single-Flush'..."),
         ("Sostituisci 'Generic' con 'Custom' nei nomi delle famiglie",
          "3 famiglie rinominate")])

    pdf.tool_card("renumber_elements",
        "Numera automaticamente elementi in sequenza: stanze, porte, finestre, parcheggi.",
        "Per assegnare numeri ordinati dopo aver posizionato gli elementi.",
        [("Numera tutte le stanze partendo da 101",
          "25 stanze numerate: 101, 102, 103... ordinate per posizione"),
         ("Numera le porte per stanza in ordine alfabetico",
          "Porte numerate: D-001 (Archivio), D-002 (Corridoio), D-003 (Ufficio 1)...")])

    # =======================================================
    # 13 - PARAMETRI AVANZATI
    # =======================================================
    pdf.section_title("13", "Parametri avanzati")

    pdf.tool_card("bulk_modify_parameter_values",
        "Modifica un parametro in massa: imposta valore, prefisso, suffisso, trova-e-sostituisci, svuota.",
        "Per aggiornare lo stesso parametro su molti elementi contemporaneamente.",
        [("Imposta il Produttore a 'ACME' per tutte le porte",
          "dryRun: 156 porte verrebbero aggiornate. Procedi? -> 156 porte aggiornate"),
         ("Aggiungi il prefisso 'STR-' al Contrassegno di tutte le travi",
          "47 travi aggiornate: 'B-01' -> 'STR-B-01'...")])

    pdf.tool_card("sync_csv_parameters",
        "Sincronizza valori di parametri da dati CSV (array di oggetti elementId + parametri).",
        "Per importare dati da fogli di calcolo o database esterni.",
        [("Aggiorna i numeri delle stanze da questi dati CSV",
          "dryRun: 25 stanze verrebbero aggiornate. -> 25 aggiornate con successo")])

    pdf.tool_card("import_from_excel",
        "Importa valori di parametri da un file Excel nel modello Revit.",
        "Per aggiornare in massa i parametri leggendo dati da un foglio Excel preparato esternamente.",
        [("Importa i dati dal file Excel delle stanze",
          "dryRun: 42 stanze verrebbero aggiornate da 'Room_Data.xlsx'. -> 42 aggiornate con successo"),
         ("Aggiorna i parametri delle porte dal file Excel",
          "Importati: 156 valori su 52 porte, 3 errori (ID non trovato)")],
        tips="Esegui prima con dryRun per verificare la corrispondenza dei dati.")

    pdf.tool_card("transfer_parameters",
        "Copia i valori dei parametri da un elemento sorgente ad altri elementi.",
        "Per propagare le proprieta' di un elemento 'modello' ad altri simili.",
        [("Copia tutti i parametri dalla porta 123 alle porte 456, 789",
          "dryRun: 8 parametri verrebbero copiati su 2 elementi -> Completato")])

    pdf.tool_card("add_prefix_suffix",
        "Aggiunge prefisso o suffisso ai valori di un parametro.",
        "Per aggiungere codici o indicatori ai parametri esistenti.",
        [("Aggiungi 'REV-' come prefisso a tutti i numeri stanza",
          "25 stanze aggiornate: '101' -> 'REV-101'...")])

    pdf.tool_card("clear_parameter_values",
        "Svuota il valore di un parametro per categoria/scope.",
        "Per resettare parametri prima di una nuova compilazione.",
        [("Svuota i Commenti di tutti i muri",
          "68 muri: parametro Comments svuotato")])

    pdf.tool_card("add_shared_parameter",
        "Aggiunge un parametro condiviso a una o piu' categorie.",
        "Per aggiungere parametri personalizzati al modello.",
        [("Aggiungi il parametro condiviso 'Stato QC' alle categorie Porte e Finestre",
          "Parametro 'Stato QC' aggiunto come istanza a Porte e Finestre")])

    pdf.tool_card("manage_project_parameters",
        "Elenca, crea o elimina parametri di progetto.",
        "Per gestire i parametri specifici del progetto.",
        [("Elenca tutti i parametri di progetto",
          "18 parametri: Stato QC (Testo), Data Verifica (Testo), Ispettore (Testo)..."),
         ("Crea un parametro 'Ispettore' di tipo Testo per le Stanze",
          "Parametro creato: 'Ispettore', tipo Testo, istanza, applicato a Stanze")])

    pdf.tool_card("get_shared_parameters",
        "Elenca i parametri condivisi nel modello.",
        "Per verificare quali parametri condivisi sono disponibili.",
        [("Elenca i parametri condivisi per la categoria Porte",
          "5 parametri condivisi: Resistenza al Fuoco, Isolamento Acustico, Stato QC...")])

    pdf.tool_card("export_shared_parameter_file",
        "Esporta le definizioni dei parametri condivisi in un file .txt.",
        "Per condividere i parametri tra progetti.",
        [("Esporta i parametri condivisi in un file",
          "File esportato: SharedParameters.txt, 18 parametri")])

    # =======================================================
    # 14 - ANALISI E QUALITA'
    # =======================================================
    pdf.section_title("14", "Analisi e qualita'")

    pdf.tool_card("check_model_health",
        "Controllo rapido della salute del modello con punteggio e problemi principali.",
        "Come prima cosa al mattino o prima di una consegna.",
        [("Com'e' la salute del modello?",
          "Score: 78/100 | 3 problemi: 45 warning, 12 famiglie non usate, 3 viste pesanti"),
         ("Il modello e' pronto per la consegna?",
          "Score: 92/100 | 1 problema minore: 5 tag vuoti")])

    pdf.tool_card("analyze_model_statistics",
        "Analisi dettagliata: conteggio elementi per categoria, tipi piu' usati.",
        "Per capire la composizione e complessita' del modello.",
        [("Quanti elementi ci sono nel modello?",
          "Totale: 12,456 elementi | Muri: 347 | Porte: 156 | Finestre: 89 | Stanze: 85..."),
         ("Mostrami le statistiche in formato compatto",
          "Compact: 12.4K elementi, 347 muri, 156 porte, 89 finestre")])

    pdf.tool_card("get_warnings",
        "Legge i messaggi di avviso (warning) attivi nel modello.",
        "Per il controllo qualita' e la risoluzione dei problemi.",
        [("Mostrami i primi 10 avvisi",
          "10 warning: 4 'Elementi sovrapposti', 3 'Altezza stanza sotto minimo', 2 'Connessione mancante'..."),
         ("Ci sono errori nel modello?",
          "0 errori, 45 avvisi. I piu' frequenti: sovrapposizione muri (12), connessioni (8)")])

    pdf.tool_card("audit_families",
        "Verifica le famiglie caricate: dimensione, numero istanze, famiglie non usate.",
        "Per identificare famiglie pesanti o da rimuovere.",
        [("Controlla le famiglie di porte",
          "8 famiglie, 24 tipi, 156 istanze | 2 tipi non usati: 'Glass Door' e 'Revolving'"),
         ("Trova le famiglie non utilizzate nel modello",
          "15 famiglie senza istanze, occupano 12MB. Vuoi fare un purge?")])

    pdf.tool_card("clash_detection",
        "Rileva interferenze geometriche tra due categorie di elementi.",
        "Per la coordinazione interdisciplinare.",
        [("Controlla interferenze tra travi e condotte",
          "8 interferenze trovate: Trave B-12 vs Condotta D-05 (overlap 45mm)..."),
         ("Ci sono muri che intersecano i pilastri?",
          "3 clash: Muro M-23 vs Pilastro P-05 (penetrazione 120mm)...")])

    pdf.tool_card("lines_per_view_count",
        "Conta le linee per vista per identificare viste troppo pesanti.",
        "Per trovare viste che rallentano il modello.",
        [("Quali viste hanno piu' linee?",
          "Top 5: Dettaglio 42 (15,230 linee), Piano Terra (8,450), Sezione A (6,200)...")])

    pdf.tool_card("list_family_sizes",
        "Elenca le famiglie per numero di istanze o tipi.",
        "Per identificare le famiglie piu' usate o piu' pesanti.",
        [("Quali sono le 20 famiglie piu' usate?",
          "Top 20: Basic Wall (347 ist.), M_Single-Flush (124 ist.), Fixed Window (89 ist.)...")])

    pdf.tool_card("get_phases",
        "Mostra le fasi del progetto e i filtri di fase.",
        "Per capire la struttura temporale del progetto.",
        [("Mostra le fasi del progetto",
          "3 fasi: Esistente (1), Demolizione (2), Nuova Costruzione (3) | 4 filtri di fase")])

    pdf.tool_card("get_worksets",
        "Elenca i workset con il numero di elementi.",
        "Per verificare l'organizzazione dei workset.",
        [("Mostra i workset del modello",
          "5 workset: Architecture (4,500 el.), Structure (2,100 el.), MEP (1,800 el.)...")])

    # =======================================================
    # 15 - PULIZIA
    # =======================================================
    pdf.section_title("15", "Pulizia del modello")

    pdf.tool_card("purge_unused",
        "Elimina famiglie, tipi e materiali non utilizzati dal modello.",
        "Per ridurre la dimensione del file e migliorare le prestazioni.",
        [("Cosa posso eliminare dal modello?",
          "dryRun: 15 famiglie, 23 tipi, 8 materiali eliminabili (-12MB)"),
         ("Pulisci tutto cio' che non e' usato",
          "Conferma Revit -> 46 elementi eliminati, file ridotto di 12MB")],
        warns="Operazione potente. Usa sempre dryRun prima per verificare.")

    pdf.tool_card("cad_link_cleanup",
        "Trova e rimuove file CAD importati o collegati.",
        "Per rimuovere DWG/DXF che appesantiscono il modello.",
        [("Elenca tutte le importazioni CAD nel modello",
          "8 importazioni: planimetria.dwg (Piano Terra), dettaglio.dwg (Sezione A)..."),
         ("Elimina tutte le importazioni DWG",
          "Conferma -> 8 importazioni CAD rimosse")])

    # =======================================================
    # 16 - WORKFLOW COMPOSITI
    # =======================================================
    pdf.section_title("16", "Workflow compositi")
    pdf.para("Questi strumenti combinano piu' operazioni in un singolo comando per task complessi.")

    pdf.tool_card("workflow_model_audit",
        "Audit completo del modello: salute + avvisi + analisi famiglie in una sola chiamata.",
        "Per un controllo qualita' completo prima della consegna.",
        [("Fai un audit completo del modello",
          "Report: Score 82/100 | 45 warning (12 critici) | 15 famiglie non usate | 3 viste pesanti"),
         ("Audit con dettaglio avvisi e famiglie",
          "Report dettagliato: warning per categoria, famiglie per dimensione, raccomandazioni")])

    pdf.tool_card("workflow_clash_review",
        "Rileva interferenze e crea automaticamente una vista 3D con section box per la revisione.",
        "Quando devi visualizzare le interferenze, non solo contarle.",
        [("Controlla le interferenze travi-condotte con vista 3D",
          "8 clash trovati. Vista 3D 'Clash Review - Beams vs Ducts' creata con section box"),
         ("Revisiona le interferenze muri-pilastri",
          "3 clash trovati, vista 3D creata per ciascun cluster")])

    pdf.tool_card("workflow_data_roundtrip",
        "Esporta dati in Excel, li modifichi esternamente, e li reimporta nel modello.",
        "Per aggiornamenti parametri in massa tramite foglio di calcolo.",
        [("Esporta i dati delle porte in Excel, li modifico e reimporto",
          "File esportato: Doors_Export.xlsx, 156 righe, 8 colonne. Modificalo e dimmi quando reimportare."),
         ("Reimporta il file Excel modificato",
          "dryRun: 23 porte verrebbero aggiornate, 2 parametri per porta. Procedi?")])

    pdf.tool_card("workflow_room_documentation",
        "Crea automaticamente sezioni per ogni stanza di un livello.",
        "Per generare rapidamente la documentazione degli spazi.",
        [("Crea sezioni per tutte le stanze del Piano 1",
          "25 sezioni create, una per stanza. Nomi: 'Section - Ufficio 101', 'Section - Corridoio 102'...")])

    pdf.tool_card("workflow_sheet_set",
        "Crea un set completo di tavole con cartiglio in un'unica operazione.",
        "Per impostare rapidamente il set documentale di un progetto.",
        [("Crea le tavole A-101 a A-105 con il cartiglio aziendale",
          "5 tavole create: A-101 Piano Terra | A-102 Piano 1 | A-103 Piano 2 | A-104 Sezioni | A-105 Prospetti")])

    # =======================================================
    # 17 - SICUREZZA
    # =======================================================
    pdf.section_title("17", "Sicurezza")
    pdf.para("RevitCortex include diverse protezioni per prevenire danni accidentali al modello.")

    pdf.h2("Finestra di conferma")
    pdf.para("Tutte le operazioni distruttive (elimina, purge, modifica parametri, rinomina) mostrano "
             "una finestra di conferma nativa di Revit prima di eseguire. Se annulli, lo strumento restituisce "
             "un errore 'Cancelled' e nulla viene modificato.")

    pdf.h2("Sandbox per il codice")
    pdf.para("Lo strumento send_code_to_revit valida il codice C# prima di eseguirlo. "
             "I seguenti namespace sono vietati e causano un errore 'PermissionDenied':")
    pdf.code("System.IO            - accesso al filesystem\n"
             "System.Net           - accesso alla rete\n"
             "System.Diagnostics   - processi di sistema\n"
             "Microsoft.Win32      - registro di Windows\n"
             "System.Reflection.Emit - generazione codice dinamico\n"
             "System.Runtime.InteropServices - interop nativo")

    pdf.h2("Modalita' sola lettura")
    pdf.para("Impostando readOnlyMode: true nel file ~/.revitcortex/settings.json, "
             "tutti gli strumenti di scrittura vengono bloccati. Solo le operazioni di lettura "
             "(get_, list_, find_, analyze_, check_, export_, audit_) rimangono attive.")

    pdf.h2("Log di audit")
    pdf.para("Ogni esecuzione di strumento viene registrata nel file ~/.revitcortex/audit.jsonl "
             "con timestamp, nome strumento, risultato e numero di elementi coinvolti.")

    # =======================================================
    # 18 - OTTIMIZZAZIONE SESSIONE
    # =======================================================
    pdf.section_title("18", "Ottimizzazione sessione")

    pdf.h2("Pattern di sessione consigliati")
    pdf.para("Non fare tutto in una sola conversazione. Dividi il lavoro in sessioni brevi e mirate:")

    pdf.h3("Sessione mattutina (2-3 chiamate)")
    pdf.prompt("", "Com'e' la salute del modello? Mostrami i 10 warning principali.")
    pdf.text_small("Strumenti: check_model_health + get_warnings. Costo: ~800 token di risposta.")

    pdf.h3("Sessione parametri (3-4 chiamate)")
    pdf.prompt("", "Esporta i dati delle porte, poi imposta il produttore 'ACME' per le porte tipo Fire Door.")
    pdf.text_small("Strumenti: export_elements_data + bulk_modify (dryRun) + bulk_modify. Costo: ~2,000 token.")

    pdf.h3("Sessione documentazione (3 chiamate)")
    pdf.prompt("", "Crea un abaco porte per stanza, esportalo come CSV, e crea le tavole A-101 a A-105.")
    pdf.text_small("Strumenti: create_preset_schedule + export_schedule + workflow_sheet_set. Costo: ~1,500 token.")

    pdf.h2("Regole d'oro")
    pdf.para("1. La prima get_project_info deve essere completa. Le successive devono filtrare.\n"
             "2. Usa maxWarnings: 10 per controlli rapidi, non il default 500.\n"
             "3. Con dryRun, leggi solo modifiedCount/skippedCount, non la lista completa.\n"
             "4. Quando il contesto supera ~15,000 token di risposta, apri una nuova conversazione.\n"
             "5. Non mischiare task QA con authoring nella stessa sessione lunga.\n"
             "6. Usa lo strumento piu' mirato: check_model_health prima di analyze_model_statistics.\n"
             "7. Per liste lunghe, chiedi a Claude le risposte in formato compatto (vedi sotto).")

    pdf.h2("Risposte compatte (compact: true)  -- novita' v1.0.18")
    pdf.para("Alcuni strumenti ad alto payload accettano un parametro compact: true che "
             "rimuove i metadati per-elemento mantenendo identificatori e contatori. "
             "Riduzione tipica dei token: 30-50%.")

    pdf.prompt("Esempio", "Elenca i materiali del progetto in formato compatto.")
    pdf.text_small("Claude chiama get_materials con compact: true. La risposta perde "
                   "transparency/shininess/smoothness ma conserva nome, classe, categoria "
                   "e gli has*Asset flags utili a decidere se interrogare get_material_properties.")

    pdf.para("Strumenti che supportano compact:")
    pdf.para("- get_element_parameters (strippa hasValue, isReadOnly, storageType, groupName, isShared)\n"
             "- get_available_family_types (strippa uniqueId, fullName, isReadOnly)\n"
             "- audit_families (strippa isInPlace, isEditable, isUnused, kind)\n"
             "- list_schedulable_fields (strippa parameterId)\n"
             "- get_room_openings (strippa metadati per apertura)\n"
             "- get_shared_parameters (strippa description)\n"
             "- get_linked_file_instances (strippa matrice di trasformazione)\n"
             "- get_elements_in_spatial_volume (strippa extras per elemento)\n"
             "- get_materials (strippa transparency/shininess/smoothness)\n"
             "- export_room_data (strippa department, perimeterMm)\n"
             "- ifc_list_export_configurations (strippa description configurazione)\n"
             "- ifc_analyze_rebuildability / ifc_list_rebuild_candidates (strippa extras)\n"
             "- workflow_model_audit (strippa dettagli warnings/famiglie verbosi)")

    pdf.para("Garanzie di sicurezza: nessun elemento viene mai rimosso dalle liste, i contatori "
             "top-level (count, totalRooms, materialCount, instanceCount) sono sempre veritieri, "
             "ID e categorie restano intatti. Default: compact: false (payload pieno).")

    pdf.h2("Prestazioni")
    pdf.para("- Operazioni di lettura: sicure in parallelo (5+)\n"
             "- Operazioni di scrittura: max 3-4 in parallelo\n"
             "- Query pesanti (analyze_model_statistics, purge_unused): eseguire singolarmente\n"
             "- create_view 3D: particolarmente pesante, evitare con altre scritture")

    pdf.h2("Lingua e localizzazione")
    pdf.para("Revit traduce i nomi di categorie e parametri in base alla lingua. "
             "RevitCortex rileva automaticamente la lingua. Usa sempre i codici OST_ "
             "(indipendenti dalla lingua) per le categorie:\n\n"
             "OST_Walls = Muri / Walls / Murs / Wande\n"
             "OST_Doors = Porte / Doors / Portes / Turen\n"
             "OST_Windows = Finestre / Windows / Fenetres / Fenster\n"
             "OST_Rooms = Vani / Rooms / Pieces / Raume\n"
             "OST_Floors = Pavimenti / Floors / Sols / Geschossdecken\n"
             "OST_StructuralFraming = Telaio strutturale / Structural Framing\n"
             "OST_StructuralColumns = Pilastri strutturali / Structural Columns")

    # =======================================================
    # SERVER-SIDE DATA TOOLS (brief section)
    # =======================================================
    pdf.h2("Strumenti di archiviazione e analisi")
    pdf.para("Questi strumenti operano lato server per memorizzare dati tra sessioni:")

    pdf.tool_card("store_project_data",
        "Salva i metadati del progetto per consultarli in sessioni future.",
        "Per mantenere uno storico dei progetti analizzati.",
        [("Salva le informazioni di questo progetto",
          "Progetto 'Edificio A' salvato nel database locale")])

    pdf.tool_card("store_room_data",
        "Salva uno snapshot dei dati delle stanze.",
        "Per confrontare le stanze nel tempo.",
        [("Salva i dati delle stanze per confrontarli dopo",
          "85 stanze salvate per il progetto 'Edificio A'")])

    pdf.tool_card("query_stored_data",
        "Interroga i dati salvati in precedenza.",
        "Per recuperare dati di sessioni passate.",
        [("Mostra i progetti salvati",
          "3 progetti: Edificio A, Scuola B, Ospedale C")])

    pdf.tool_card("report_token_usage",
        "Report sull'utilizzo degli strumenti MCP.",
        "Per capire quali strumenti usi di piu'.",
        [("Quali strumenti ho usato di piu' questa settimana?",
          "Top 5: get_element_parameters (45x), filter_by_parameter_value (23x), export_elements_data (18x)...")])

    pdf.tool_card("analyze_journal",
        "Analizza i file journal di Revit per diagnostica.",
        "Per investigare crash, problemi di memoria, o comportamenti anomali.",
        [("Analizza le ultime 3 sessioni di Revit",
          "Sessione 1: 45 min, 3.2GB RAM peak, 12 transazioni | Sessione 2: crash dopo 23 min...")])

    # =======================================================
    # 19 - IFC
    # =======================================================
    pdf.section_title("19", "IFC - Importazione, Esportazione e Ricostruzione")
    pdf.para("IFC (Industry Foundation Classes) e' il formato aperto per l'interoperabilita' BIM. "
             "RevitCortex offre tre gruppi di strumenti: "
             "importazione/collegamento, esportazione configurabile, "
             "e ricostruzione nativa - il workflow piu' avanzato che converte gli elementi IFC "
             "in elementi Revit nativi editabili.")

    pdf.h2("Verifica e diagnostica")

    pdf.tool_card("ifc_get_capabilities",
        "Verifica il supporto IFC di questa installazione Revit: versioni supportate, add-in revit-ifc.",
        "Prima di lavorare con IFC, per sapere cosa e' disponibile.",
        [("Quali versioni IFC supporta questa installazione di Revit?",
          "IFC 2x3, IFC 4, IFC 4x3 supportati | revit-ifc add-in: v24.1.0 | Schema: ifcXML supportato"),
         ("Revit supporta IFC 4.3?",
          "IFC 4x3 supportato (add-in v24.1.0). Export e import disponibili.")])

    pdf.tool_card("ifc_validate_request",
        "Valida un file IFC prima di importarlo: controlla il percorso, l'estensione e la versione dello schema.",
        "Prima di importare o collegare un file IFC per intercettare errori subito.",
        [("Controlla se il file C:/Modelli/Edificio_Strutture.ifc e' valido",
          "OK: file trovato, estensione .ifc valida, schema IFC 2x3 rilevato"),
         ("Verifica il file IFC prima di collegarlo al progetto",
          "OK: schema IFC 4, dimensione 45MB, struttura valida")])

    pdf.h2("Importazione e collegamento")

    pdf.tool_card("ifc_link",
        "Collega un file IFC al progetto come link esterno (rimane aggiornabile).",
        "Quando vuoi fare riferimento al modello IFC senza incorporarlo nel progetto.",
        [("Collega il modello strutturale IFC dal percorso C:/Modelli/Strutture.ifc",
          "Link IFC aggiunto: 'Strutture.ifc', 1.245 elementi, sistema di coordinate condivise"),
         ("Aggiungi il file IFC dell'impianto MEP come collegamento",
          "Link IFC 'MEP.ifc' collegato: 3.102 elementi, posizione origine")])

    pdf.tool_card("ifc_reload_link",
        "Ricarica un collegamento IFC esistente, opzionalmente da un nuovo percorso.",
        "Quando il file IFC e' stato aggiornato dal consulente strutturale o MEP.",
        [("Ricarica il link IFC strutturale",
          "Link 'Strutture.ifc' ricaricato: 1.287 elementi (+42 rispetto alla versione precedente)"),
         ("Ricarica il link MEP dal nuovo percorso aggiornato",
          "Link ricaricato dal nuovo percorso, 3.150 elementi aggiornati")])

    pdf.tool_card("ifc_open_or_import",
        "Apre un file IFC come nuovo progetto Revit o lo importa nel documento attivo.",
        "Quando vuoi incorporare definitivamente il modello IFC nel progetto.",
        [("Apri il file IFC come nuovo progetto Revit",
          "File aperto come nuovo progetto Revit, 2.456 elementi convertiti in DirectShape"),
         ("Importa il file IFC nel progetto corrente",
          "1.245 DirectShape importati nel documento attivo")])

    pdf.h2("Esportazione")

    pdf.tool_card("ifc_list_export_configurations",
        "Elenca le configurazioni di esportazione IFC disponibili (predefinite e personalizzate).",
        "Prima di esportare, per scegliere la configurazione giusta.",
        [("Quali configurazioni IFC posso usare per l'export?",
          "6 configurazioni: IFC2x3 Coordination View, IFC4 Reference View, IFC4 Design Transfer, GSA (2010), COBie 2.4, Custom_Arch")])

    pdf.tool_card("ifc_get_export_configuration",
        "Mostra tutti i dettagli di una specifica configurazione di esportazione IFC.",
        "Per verificare i parametri di una configurazione prima di usarla.",
        [("Mostrami i dettagli della configurazione 'IFC4 Design Transfer'",
          "Schema: IFC4 | Classificazione: OmniClass | Export di: muri, porte, finestre, spazi | Includere quantita': si'")])

    pdf.tool_card("ifc_export_basic",
        "Esporta il modello in IFC con opzioni base: versione schema, percorso di output.",
        "Per export rapido senza configurazioni complesse.",
        [("Esporta il modello come IFC 4 nella cartella C:/Consegne",
          "Export completato: Edificio_A.ifc (45MB), schema IFC4, 3.456 elementi"),
         ("Esporta in IFC 2x3 per compatibilita' con sistemi vecchi",
          "Export IFC 2x3 completato: 3.201 elementi, formato compatibile")])

    pdf.tool_card("ifc_export_with_configuration",
        "Esporta usando una configurazione nominata con la possibilita' di override specifici.",
        "Per export professionale con parametri controllati.",
        [("Esporta usando la configurazione 'IFC4 Design Transfer' con classificazione OmniClass",
          "Export con configurazione 'IFC4 Design Transfer': 3.456 elementi, 156 spazi, classificazione OmniClass applicata"),
         ("Esporta solo il Piano Terra con la configurazione COBie 2.4",
          "Export COBie completato: 245 elementi Piano Terra, foglio COBie generato")])

    pdf.tool_card("ifc_set_family_mapping_file",
        "Imposta un file di mapping per associare le famiglie Revit ai tipi IFC corretti.",
        "Per garantire che le famiglie vengano esportate con il tipo IFC appropriato.",
        [("Imposta il file di mapping famiglie dal percorso C:/Config/FamilyMapping.txt",
          "File di mapping impostato: 45 regole di associazione famiglia->tipo IFC caricate")])

    pdf.h2("Ricostruzione nativa (IFC -> Revit nativo)")
    pdf.para("Quando si importa un file IFC, Revit crea dei DirectShape  - oggetti geometrici non editabili. "
             "Il workflow di ricostruzione analizza questi elementi e li converte in elementi Revit nativi "
             "(muri, pavimenti, travi, porte) completamente modificabili.")
    pdf.tip("Workflow consigliato: analizza -> elenca candidati -> ricostruisci per categoria -> taglia aperture -> piazza porte/finestre -> confronta -> tagga i non ricostruibili.")

    pdf.tool_card("ifc_analyze_rebuildability",
        "Analizza i DirectShape IFC nel modello e calcola per ciascuno la fattibilita' di ricostruzione nativa.",
        "Primo passo del workflow di ricostruzione: capire quanti elementi possono essere convertiti.",
        [("Analizza il modello IFC importato: quanti elementi posso ricostruire?",
          "Analisi completata: 1.245 DirectShape | Muri: 312 (89% ricostruibili) | Pavimenti: 45 (95%) | Travi: 67 (78%) | Non ricostruibili: 123 (10%)"),
         ("Verifica la ricostruibilita' solo per i muri",
          "312 muri analizzati: 278 ricostruibili (confidenza >80%), 34 complessi (possibile perdita geometrica)")])

    pdf.tool_card("ifc_list_rebuild_candidates",
        "Elenca gli elementi con confidenza di ricostruzione sopra una soglia specificata.",
        "Per decidere quali elementi ricostruire partendo da quelli piu' sicuri.",
        [("Mostra tutti gli elementi ricostruibili con confidenza almeno 85%",
          "847 candidati: 278 muri, 43 pavimenti, 62 travi, 45 pilastri (confidenza min: 85%)"),
         ("Quali muri posso ricostruire con sicurezza?",
          "278 muri con confidenza >80%: 245 rettangolari semplici, 33 con sagoma irregolare")])

    pdf.tool_card("ifc_rebuild_walls",
        "Ricostruisce i muri da DirectShape IFC in muri Revit nativi con spessore e tipo corretti.",
        "Dopo l'analisi, per convertire i muri IFC in muri Revit modificabili.",
        [("Ricostruisci tutti i muri dall'IFC importato",
          "278 muri ricostruiti come muri Revit nativi: tipo 'Basic Wall 200mm' (142), 'Basic Wall 300mm' (89), altri (47)"),
         ("Ricostruisci solo i muri al Piano Terra",
          "68 muri Piano Terra ricostruiti, livello di base impostato a 'Piano Terra'")])

    pdf.tool_card("ifc_rebuild_floors",
        "Ricostruisce i pavimenti da DirectShape IFC in solai Revit nativi.",
        "Per convertire i pavimenti IFC in elementi editabili con stratigrafia.",
        [("Ricostruisci i pavimenti dal modello IFC",
          "43 pavimenti ricostruiti: tipo 'Generic Floor 200mm', area totale 4.560 mq"),
         ("Ricostruisci i solai con il tipo di pavimento architettonico",
          "43 pavimenti ricostruiti con tipo 'Architectural Floor 150mm'")])

    pdf.tool_card("ifc_rebuild_roofs",
        "Ricostruisce i tetti da DirectShape IFC in tetti Revit nativi.",
        "Per convertire la copertura IFC in un elemento editabile con pendenze.",
        [("Ricostruisci la copertura dal file IFC",
          "5 falde ricostruite come tetto Revit nativo, pendenze calcolate automaticamente"),
         ("Ricostruisci il tetto piano",
          "1 tetto piano ricostruito: tipo 'Flat Roof 300mm', area 980 mq")])

    pdf.tool_card("ifc_rebuild_structural_members",
        "Ricostruisce colonne e travi da DirectShape IFC in elementi strutturali Revit nativi.",
        "Per convertire il telaio strutturale IFC in elementi analizzabili e modificabili.",
        [("Ricostruisci tutto il telaio strutturale dall'IFC",
          "Completato: 45 pilastri HEB240/300 ricostruiti, 67 travi IPE300/400 ricostruite"),
         ("Ricostruisci solo le travi del Piano 2",
          "22 travi Piano 2 ricostruite: HEB300 (12), IPE400 (10), campata media 7.2m")])

    pdf.tool_card("ifc_rebuild_openings",
        "Taglia le aperture nelle pareti e nei solai ricostruiti, in corrispondenza delle aperture IFC originali.",
        "Dopo aver ricostruito muri e solai, per creare le aperture corrette.",
        [("Taglia le aperture nei muri ricostruiti",
          "234 aperture tagliate: 156 per porte, 78 per finestre"),
         ("Crea le aperture anche nei solai ricostruiti",
          "12 aperture per vano scala e impianti tagliate nei solai")])

    pdf.tool_card("ifc_rebuild_family_instances",
        "Posiziona porte e finestre nelle aperture ricostruite, usando famiglie Revit native.",
        "Ultimo passo di ricostruzione: popolare le aperture con elementi nativi.",
        [("Posiziona porte e finestre nelle aperture ricostruite",
          "156 porte posizionate (M_Single-Flush 900x2100), 78 finestre posizionate (Fixed 1200x1500)"),
         ("Piazza le porte usando la famiglia 'Fire Door EI60' per le aperture tagliafuoco",
          "45 porte tagliafuoco posizionate, 111 porte standard")])

    pdf.tool_card("ifc_compare_original_vs_rebuilt",
        "Confronta volume e geometria tra gli elementi IFC originali e quelli Revit ricostruiti.",
        "Per verificare la fedeltà della ricostruzione prima di eliminare i DirectShape.",
        [("Confronta gli elementi ricostruiti con gli originali IFC",
          "Media fedeltà: 97.3% | Muri: 98.1% | Pavimenti: 96.8% | 12 elementi con scarto >5% segnalati"),
         ("Verifica la fedeltà dei muri ricostruiti",
          "278 muri confrontati: 265 (95%) fedeltà >95%, 13 con divergenza geometrica da verificare")])

    pdf.tool_card("ifc_tag_unreconstructable_elements",
        "Tagga gli elementi IFC che non possono essere ricostruiti come nativi (geometria troppo complessa).",
        "Per documentare quali elementi restano come DirectShape e devono essere gestiti manualmente.",
        [("Tagga tutti gli elementi che non ho potuto ricostruire",
          "123 elementi taggati come 'IFC_Non_Ricostruibile': 45 scale, 34 rampe, 44 geometrie complesse"),
         ("Aggiungi un parametro 'Motivo Non Ricostruzione' agli elementi non ricostruiti",
          "123 elementi: parametro aggiunto con motivazione (scala, geometria curva, ecc.)")])

    # =======================================================
    # APPENDICE  - LIBRERIA DI PROMPT
    # =======================================================
    pdf.add_page()
    pdf.set_font("Helvetica", "B", 22)
    pdf.set_text_color(*C_PRIMARY)
    pdf.cell(0, 14, "Appendice - Libreria di Prompt", new_x="LMARGIN", new_y="NEXT", align="C")
    pdf.ln(2)
    pdf.set_font("Helvetica", "", 10)
    pdf.set_text_color(*C_GRAY)
    pdf.multi_cell(0, 5.5, "Prompt pronti all'uso organizzati per scenario. Copia, adatta al tuo progetto e incollali in Claude.")
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
        ("ORIENTARSI IN UN MODELLO NUOVO", [
            ("Panoramica completa", "Cos'e' questo progetto? Mostrami livelli, workset, fasi e file collegati."),
            ("Statistiche rapide", "Quanti elementi ci sono per categoria? Mostrami le prime 10 categorie."),
            ("Vista attiva", "In che vista sono? Quanti elementi sono visibili?"),
            ("Salute del modello", "Fai un check rapido della salute del modello e mostrami i 5 avvisi piu' importanti."),
        ]),
        ("CERCARE E FILTRARE ELEMENTI", [
            ("Per categoria", "Trova tutti i [pilastri strutturali] nel modello, mostrami tipo e livello."),
            ("Per parametro", "Trova tutte le porte dove il campo 'Resistenza al fuoco' e' vuoto."),
            ("Per valore", "Quali muri al Piano 1 hanno spessore maggiore di 250mm?"),
            ("Nella vista attiva", "Elenca tutti i muri visibili in questa vista con tipo e lunghezza."),
            ("Elemento selezionato", "Cosa ho selezionato? Mostrami tutti i parametri."),
        ]),
        ("MODIFICARE PARAMETRI", [
            ("Singolo elemento", "Imposta il Contrassegno della porta 12345 a 'D-001'."),
            ("In massa (sicuro)", "Quante porte verrebbero modificate se imposto il Produttore a 'ACME'? (anteprima)"),
            ("In massa (esegui)", "OK, procedi: imposta il Produttore 'ACME' per tutte le porte."),
            ("Da Excel", "Importa i valori di parametro dal file Excel 'DoorData.xlsx' (prima mostrami anteprima)."),
            ("Rinomina", "Rinomina tutte le viste sostituendo 'Copia di ' con '' (rimuovi il prefisso)."),
            ("Numera stanze", "Numera tutte le stanze partendo da 101, in ordine da sinistra a destra."),
        ]),
        ("CREARE ELEMENTI", [
            ("Muro", "Crea un muro di tipo 'Basic Wall 200mm' dal punto (0,0) al punto (8,0) al Piano Terra."),
            ("Pavimento", "Crea un pavimento architettonico nella stanza 205 con tipo 'Concrete 150mm'."),
            ("Porta", "Posiziona una porta 'M_Single-Flush 900x2100' nel muro 5678 a 1.5m dall'estremita' sinistra."),
            ("Stanza", "Crea una stanza 'Ufficio Direttore' numero 101 al Piano 1 alle coordinate (12, 8)."),
            ("Livello", "Crea il Piano 3 a quota 10.5 metri."),
            ("Griglia", "Crea una griglia 4x3 con passo 6 metri, assi A-D e 1-3."),
        ]),
        ("VISTE E TAVOLE", [
            ("Pianta", "Crea una pianta del Piano 2 in scala 1:100."),
            ("Sezione", "Crea una sezione che taglia l'edificio da ovest a est al Piano Terra."),
            ("Vista 3D isolata", "Crea una vista 3D con section box attorno agli elementi selezionati."),
            ("Tavole in batch", "Crea le tavole A-101, A-102, A-103 con il cartiglio 'Company Titleblock'."),
            ("Posiziona vista", "Metti la pianta del Piano 1 sulla tavola A-101, centrata."),
            ("Viste per stanze", "Crea una sezione per ogni stanza del Piano 1."),
            ("Esporta DWG", "Esporta tutte le tavole in DWG nella cartella C:/Consegne."),
        ]),
        ("ABACHI E DATI", [
            ("Abaco rapido", "Crea un abaco delle porte con Nome tipo, Livello e Resistenza al fuoco."),
            ("Abaco stanze", "Crea un elenco stanze con nome, numero, area e dipartimento."),
            ("Esporta CSV", "Esporta l'abaco porte come file CSV."),
            ("Esporta Excel", "Esporta tutti i dati delle porte in un file Excel sul desktop."),
            ("Leggi abaco", "Mostrami le prime 20 righe dell'abaco 'Abaco Porte'."),
        ]),
        ("QUALITA' E AUDIT", [
            ("Check mattutino", "Com'e' la salute del modello? Score e problemi principali."),
            ("Warning", "Mostrami i 10 avvisi piu' frequenti nel modello."),
            ("Clash detection", "Controlla interferenze tra travi e condotte. Crea una vista 3D per le interferenze trovate."),
            ("Famiglie pesanti", "Quali sono le 10 famiglie piu' usate? Ci sono famiglie non utilizzate?"),
            ("Prima della consegna", "Fai un audit completo: salute, avvisi critici, famiglie non usate, viste pesanti."),
        ]),
        ("MATERIALI E STRATIGRAFIE", [
            ("Elenco materiali", "Elenca tutti i materiali nel modello raggruppati per classe."),
            ("Stratigrafia", "Mostra la composizione a strati del muro 'Muro Esterno 300mm'."),
            ("Crea materiale", "Crea un materiale 'Vetro Serigrafato' con trasparenza 30% e colore grigio chiaro."),
            ("Quantita'", "Quanti mq di calcestruzzo sono usati nel modello?"),
        ]),
        ("IFC  - IMPORTAZIONE E ESPORTAZIONE", [
            ("Verifica supporto", "Quali versioni IFC supporta questa installazione di Revit?"),
            ("Valida file", "Controlla se il file C:/Consegne/Strutture.ifc e' valido prima di collegarlo."),
            ("Collega IFC", "Collega il modello strutturale IFC dal file C:/Modelli/Strutture.ifc."),
            ("Esporta IFC4", "Esporta il modello come IFC 4 nella cartella C:/Consegne."),
            ("Config export", "Esporta usando la configurazione 'IFC4 Design Transfer'."),
        ]),
        ("IFC  - RICOSTRUZIONE NATIVA", [
            ("Analisi", "Analizza gli elementi IFC importati: quanti posso ricostruire come elementi Revit nativi?"),
            ("Candidati", "Elenca tutti gli elementi con confidenza di ricostruzione sopra l'85%."),
            ("Ricostruisci muri", "Ricostruisci tutti i muri dall'IFC importato come muri Revit nativi."),
            ("Ricostruisci tutto", "Ricostruisci muri, pavimenti, travi e pilastri. Poi taglia le aperture e piazza porte e finestre."),
            ("Confronta", "Confronta la geometria degli elementi ricostruiti con gli originali IFC. Qual e' la fedeltà media?"),
            ("Tagga residui", "Tagga gli elementi IFC che non possono essere ricostruiti come nativi."),
        ]),
        ("PULIZIA E FINE PROGETTO", [
            ("Purge", "Cosa posso eliminare dal modello? Famiglie, tipi e materiali non usati. (anteprima prima)"),
            ("CAD inutili", "Elenca tutti i file CAD importati nel modello. Poi elimina quelli non piu' necessari."),
            ("Viste orfane", "Mostrami le viste non posizionate su nessuna tavola. Vuoi eliminare quelle di lavoro?"),
            ("Tag vuoti", "Rimuovi tutti i tag con valore vuoto dalla vista corrente."),
            ("Workset", "Sposta tutte le porte nel workset 'Architettura - Porte'."),
        ]),
        ("SCRIPT C# PERSONALIZZATO (Revit 2025+)", [
            ("Operazione batch", "Aggiungi il prefisso 'STD-' a tutte le famiglie di tipo 'Generic'."),
            ("Tipo complesso", "Scrivi un codice C# per creare un tipo pavimento 'PV_001' con 3 strati: gres 10mm, massetto 60mm, CLS 150mm."),
            ("Lettura avanzata", "Calcola l'area totale di tutti i muri per livello e restituisci una tabella."),
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
    out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "docs", "RevitCortex_User_Guide_IT.pdf")
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    pdf.output(out_path)
    print(f"PDF generated: {out_path}")
    print(f"Pages: {pdf.page_no()}")


if __name__ == "__main__":
    build_pdf()
