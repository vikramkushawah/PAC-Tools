# PAC Tools - builds the user guide, docs/guide/PAC-Tools-Guide.pdf.
#
#     pip install reportlab
#     python docs/guide/build_guide.py
#
# The PDF is committed, so building the add-in does not need Python. When a tool changes, edit the text
# here, run this, and commit both files. The version is read from PacTools.csproj, so it cannot drift.
import re
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.platypus import (
    BaseDocTemplate, CondPageBreak, Flowable, Frame, KeepTogether, ListFlowable, ListItem, PageBreak,
    PageTemplate, Paragraph, Spacer, Table, TableStyle,
)

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
OUTPUT = HERE / "PAC-Tools-Guide.pdf"
VERSION = re.search(r"<Version>([^<]+)</Version>", (ROOT / "src/PacTools/PacTools.csproj").read_text("utf-8")).group(1)

# The same colours as Core/Brand.cs and the windows' XAML.
ACCENT = colors.HexColor("#0F5E8C")
ACCENT_DARK = colors.HexColor("#0A4463")
ACCENT_PALE = colors.HexColor("#CFE3EF")
TINT = colors.HexColor("#EEF5F9")
MUTED = colors.HexColor("#5B6670")
LINE = colors.HexColor("#DDE2E6")
PANEL = colors.HexColor("#F6F8F9")
INK = colors.HexColor("#1F2933")
AMBER = colors.HexColor("#FFF6E0")
AMBER_LINE = colors.HexColor("#E8C468")

MARGIN = 20 * mm
WIDTH = A4[0] - 2 * MARGIN

body = ParagraphStyle("body", fontName="Helvetica", fontSize=10, leading=14.5, textColor=INK, spaceAfter=6, alignment=TA_LEFT)
h1 = ParagraphStyle("h1", parent=body, fontName="Helvetica-Bold", fontSize=16, leading=20, textColor=ACCENT_DARK, spaceBefore=14, spaceAfter=6)
h2 = ParagraphStyle("h2", parent=body, fontName="Helvetica-Bold", fontSize=11.5, leading=15, textColor=ACCENT, spaceBefore=9, spaceAfter=3)
cell = ParagraphStyle("cell", parent=body, fontSize=9, leading=12.5, spaceAfter=0)
head = ParagraphStyle("head", parent=cell, fontName="Helvetica-Bold", textColor=colors.white)
caption = ParagraphStyle("caption", parent=body, fontSize=8.5, leading=11, textColor=MUTED, spaceBefore=3, spaceAfter=9)
note = ParagraphStyle("note", parent=cell, leading=13)


def P(text, style=body):
    return Paragraph(text, style)


def code(text, size=9):
    return '<font face="Courier" size="%s">' % size + text + "</font>"


def steps(items, kind="1"):
    return ListFlowable(
        [ListItem(P(t), leftIndent=16) for t in items],
        bulletType="1" if kind == "1" else "bullet",
        start=None if kind == "1" else "•",
        bulletFontSize=10, leftIndent=16,
    )


def bullets(items):
    return steps(items, kind="bullet")


def table(rows, widths):
    data = [[P(c, head) for c in rows[0]]] + [[P(c, cell) for c in r] for r in rows[1:]]
    t = Table(data, colWidths=widths, repeatRows=1)
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), ACCENT),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LINEBELOW", (0, 1), (-1, -1), 0.5, LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 6), ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 4), ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
    ]))
    return t


def callout(text):
    t = Table([[P(text, note)]], colWidths=[WIDTH])
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), AMBER), ("BOX", (0, 0), (-1, -1), 0.75, AMBER_LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 9), ("RIGHTPADDING", (0, 0), (-1, -1), 9),
        ("TOPPADDING", (0, 0), (-1, -1), 6), ("BOTTOMPADDING", (0, 0), (-1, -1), 7),
    ]))
    return t


class Ribbon(Flowable):
    """A drawing of the PAC Tools ribbon tab. Drawn, not a screenshot, so it stays sharp and in step."""

    def wrap(self, aw, ah):
        self.width = aw
        self.height = 34 * mm
        return aw, self.height

    def draw(self):
        c = self.canv
        w, h = self.width, self.height
        tab_h = 7 * mm

        # The tab strip, with the PAC Tools tab active.
        c.setFillColor(PANEL)
        c.setStrokeColor(LINE)
        c.rect(0, h - tab_h, w, tab_h, fill=1, stroke=1)
        c.setFillColor(MUTED)
        c.setFont("Helvetica", 8.5)
        x = 4 * mm
        for name in ("Architecture", "Structure", "...", "TCPL BIM Tools"):
            c.drawString(x, h - tab_h + 2.2 * mm, name)
            x += c.stringWidth(name, "Helvetica", 8.5) + 6 * mm
        tab_w = 22 * mm
        c.setFillColor(colors.white)
        c.setStrokeColor(ACCENT)
        c.rect(x - 2 * mm, h - tab_h, tab_w, tab_h, fill=1, stroke=1)
        c.setFillColor(ACCENT_DARK)
        c.setFont("Helvetica-Bold", 8.5)
        c.drawCentredString(x - 2 * mm + tab_w / 2, h - tab_h + 2.2 * mm, "PAC Tools")

        # The body of the ribbon.
        c.setFillColor(colors.white)
        c.setStrokeColor(LINE)
        c.rect(0, 0, w, h - tab_h, fill=1, stroke=1)

        def panel(x0, x1, title):
            c.setStrokeColor(LINE)
            c.line(x1, 1.5 * mm, x1, h - tab_h - 1.5 * mm)
            c.setFillColor(MUTED)
            c.setFont("Helvetica", 8)
            c.drawCentredString((x0 + x1) / 2, 1.8 * mm, title)

        def label(cx, lines):
            c.setFillColor(INK)
            c.setFont("Helvetica", 8)
            y = 11 * mm - 3 * mm * (len(lines) - 1) / 2 + 0.5 * mm
            for i, line in enumerate(lines):
                c.drawCentredString(cx, y - 3 * mm * i + 3 * mm * (len(lines) - 1) - 1.5 * mm, line)

        top = h - tab_h - 11 * mm  # bottom of the icons

        # Element Count: three bars.
        cx = 15 * mm
        c.setFillColor(ACCENT)
        for dx, bar in ((-3.4, 2.5), (-1, 4.4), (1.4, 6.2)):
            c.rect(cx + dx * mm, top, 2 * mm, bar * mm, fill=1, stroke=0)
        c.setFillColor(ACCENT_DARK)
        c.rect(cx - 4 * mm, top - 0.4 * mm, 8 * mm, 0.4 * mm, fill=1, stroke=0)
        label(cx, ["Element", "Count"])
        panel(0, 30 * mm, "Model")

        # Guide: an open book.
        cx = 42 * mm
        c.setFillColor(ACCENT)
        c.rect(cx - 4.5 * mm, top, 4.2 * mm, 6.2 * mm, fill=1, stroke=0)
        c.rect(cx + 0.3 * mm, top, 4.2 * mm, 6.2 * mm, fill=1, stroke=0)
        c.setStrokeColor(colors.white)
        c.setLineWidth(0.5)
        for dy in (1.6, 3.1, 4.6):
            c.line(cx - 3.7 * mm, top + dy * mm, cx - 1.1 * mm, top + dy * mm)
            c.line(cx + 1.1 * mm, top + dy * mm, cx + 3.7 * mm, top + dy * mm)
        c.setLineWidth(1)
        label(cx, ["Guide"])

        # About: a circle with an i.
        cx = 57 * mm
        c.setFillColor(ACCENT)
        c.circle(cx, top + 3.1 * mm, 3.1 * mm, fill=1, stroke=0)
        c.setFillColor(colors.white)
        c.circle(cx, top + 4.7 * mm, 0.45 * mm, fill=1, stroke=0)
        c.rect(cx - 0.35 * mm, top + 1.2 * mm, 0.7 * mm, 2.4 * mm, fill=1, stroke=0)
        label(cx, ["About"])
        panel(30 * mm, 68 * mm, "Help")


class CountWindow(Flowable):
    """A drawing of the Element Count window, laid out from UI/ElementCountWindow.xaml. Numbers are invented."""

    def wrap(self, aw, ah):
        self.width = 112 * mm
        self.height = 84 * mm
        return self.width, self.height

    def draw(self):
        c = self.canv
        w, h = self.width, self.height
        pad = 4 * mm

        c.setFillColor(colors.white)
        c.setStrokeColor(LINE)
        c.rect(0, 0, w, h, fill=1, stroke=1)

        # 1. Heading.
        c.setFillColor(ACCENT)
        c.rect(0, h - 14 * mm, w, 14 * mm, fill=1, stroke=0)
        c.setFillColor(colors.white)
        c.setFont("Helvetica-Bold", 12)
        c.drawString(pad, h - 6.5 * mm, "Element Count")
        c.setFillColor(ACCENT_PALE)
        c.setFont("Helvetica", 8)
        c.drawString(pad, h - 11 * mm, "Sample project  -  Level 1 Framing Plan")

        # 2. Scope.
        y = h - 20 * mm
        c.setFont("Helvetica", 8.5)
        c.setFillColor(MUTED)
        c.drawString(pad, y, "Count:")
        c.setStrokeColor(MUTED)
        c.setFillColor(ACCENT)
        c.circle(pad + 14 * mm, y + 1 * mm, 1.2 * mm, fill=1, stroke=1)
        c.setFillColor(colors.white)
        c.circle(pad + 37 * mm, y + 1 * mm, 1.2 * mm, fill=1, stroke=1)
        c.setFillColor(INK)
        c.drawString(pad + 16.5 * mm, y, "Active view")
        c.drawString(pad + 39.5 * mm, y, "Whole model")

        # 3. The table.
        rows = [("Structural Framing", "412", "41.2 %"), ("Structural Columns", "186", "18.6 %"),
                ("Grids", "148", "14.8 %"), ("Dimensions", "134", "13.4 %"), ("Text Notes", "120", "12.0 %")]
        top = h - 24 * mm
        row_h = 5.2 * mm
        c.setFillColor(PANEL)
        c.rect(pad, top - row_h, w - 2 * pad, row_h, fill=1, stroke=0)
        c.setFont("Helvetica-Bold", 8)
        c.setFillColor(INK)
        c.drawString(pad + 2 * mm, top - row_h + 1.6 * mm, "Category")
        c.drawRightString(w - pad - 21 * mm, top - row_h + 1.6 * mm, "Elements")
        c.drawRightString(w - pad - 3 * mm, top - row_h + 1.6 * mm, "Share")
        c.setFont("Helvetica", 8)
        for i, (name, n, share) in enumerate(rows):
            yy = top - row_h * (i + 2) + 1.6 * mm
            c.setFillColor(INK)
            c.drawString(pad + 2 * mm, yy, name)
            c.drawRightString(w - pad - 21 * mm, yy, n)
            c.setFillColor(MUTED)
            c.drawRightString(w - pad - 3 * mm, yy, share)
        c.setStrokeColor(LINE)
        c.rect(pad, top - row_h * 6 - 3 * mm, w - 2 * pad, row_h * 6 + 3 * mm, fill=0, stroke=1)

        # 4. Summary.
        y = top - row_h * 6 - 8 * mm
        c.setFillColor(INK)
        c.setFont("Helvetica-Bold", 8.5)
        c.drawString(pad, y, "1,000 elements in 5 categories in the active view.")
        c.setFillColor(MUTED)
        c.setFont("Helvetica", 8)
        c.drawString(pad, y - 4 * mm, "Read at 15:04:22")

        # 5. Buttons.
        c.setFillColor(PANEL)
        c.rect(0, 0, w, 11 * mm, fill=1, stroke=0)
        c.setStrokeColor(LINE)
        c.line(0, 11 * mm, w, 11 * mm)
        c.setFillColor(MUTED)
        c.setFont("Helvetica", 6.5)
        c.drawString(pad, 3.2 * mm, "Read only.")
        x = w - pad
        for name in ("Close", "Save CSV...", "Copy CSV", "Refresh"):
            bw = 19 * mm
            x -= bw
            c.setFillColor(colors.white)
            c.setStrokeColor(MUTED)
            c.rect(x, 2.5 * mm, bw, 6 * mm, fill=1, stroke=1)
            c.setFillColor(INK)
            c.setFont("Helvetica", 7.5)
            c.drawCentredString(x + bw / 2, 4.6 * mm, name)
            x -= 1.2 * mm


def cover(canvas, doc):
    canvas.saveState()
    w, h = A4
    canvas.setFillColor(ACCENT)
    canvas.rect(0, h - 66 * mm, w, 66 * mm, fill=1, stroke=0)
    canvas.setFillColor(colors.white)
    canvas.setFont("Helvetica-Bold", 30)
    canvas.drawString(MARGIN, h - 34 * mm, "PAC Tools")
    canvas.setFont("Helvetica", 14)
    canvas.drawString(MARGIN, h - 44 * mm, "User guide")
    canvas.setFillColor(ACCENT_PALE)
    canvas.setFont("Helvetica", 10)
    canvas.drawString(MARGIN, h - 54 * mm, "Version " + VERSION + "  |  Revit 2025 and 2026  |  Tandon Consultants Pvt. Ltd.")
    footer(canvas, doc)
    canvas.restoreState()


def footer(canvas, doc):
    canvas.saveState()
    canvas.setStrokeColor(LINE)
    canvas.line(MARGIN, 14 * mm, A4[0] - MARGIN, 14 * mm)
    canvas.setFillColor(MUTED)
    canvas.setFont("Helvetica", 8)
    canvas.drawString(MARGIN, 10 * mm, "PAC Tools " + VERSION + " user guide")
    canvas.drawRightString(A4[0] - MARGIN, 10 * mm, "Page %d" % doc.page)
    canvas.restoreState()


def story():
    s = []

    # Page 1 starts below the cover band; see the frames in build().
    s += [
        P("1. What PAC Tools is", h1),
        P("PAC Tools is a Revit add-in by Tandon Consultants Pvt. Ltd. It adds its own <b>PAC Tools</b> tab to the "
          "Revit ribbon. Its purpose is to connect Excel spreadsheets to Revit schedules."),
        callout("<b>What works in this version.</b> The Excel tools are not available yet. Version " + VERSION +
                " contains <b>Element Count</b>, <b>Guide</b> and <b>About</b>. Section 6 describes what is planned."),
        Spacer(1, 6),
        P("The PAC Tools tab", h2),
        Ribbon(),
        P("The tab and its buttons. This is a drawing, not a screenshot.", caption),
        table([
            ["Button", "Panel", "What it does", "Changes the model?"],
            ["Element Count", "Model", "Counts elements per category, for the active view or the whole model.", "No"],
            ["Guide", "Help", "Opens this PDF.", "No"],
            ["About", "Help", "Shows the version number and where the log file is.", "No"],
        ], [30 * mm, 18 * mm, WIDTH - 83 * mm, 35 * mm]),

        P("2. Install, update and remove", h1),
        P("You need", h2),
        bullets([
            "Revit 2025 or 2026. Older versions are not supported.",
            "The .NET 8 SDK, because the installer builds the add-in on your PC. The SDK is needed, not only the runtime.",
            "A copy of the PAC-Tools repository. Administrator rights are not needed: files go only into your own user profile.",
        ]),
        CondPageBreak(45 * mm),
        P("Install or update", h2),
        steps([
            "<b>Save your work and close Revit.</b> The installer stops if Revit is running.",
            "Double-click " + code("install\\Install-PACTools.bat") + " in the repository. It builds the add-in, copies it in, and "
            "checks that the installed copy matches the build.",
            "Start Revit. If it asks about an unknown add-in, choose <b>Always Load</b>.",
            "Open a project and look for the <b>PAC Tools</b> tab.",
        ]),
        P("For Revit 2026, run this from a terminal in the repository folder instead:", ParagraphStyle("t", parent=body, spaceBefore=6)),
        P(code("powershell -NoProfile -ExecutionPolicy Bypass -File install\\install.ps1 -RevitVersion 2026", 8)),
        P("Remove", h2),
        P("Close Revit and double-click " + code("install\\Uninstall-PACTools.bat") + ". This removes the add-in only. "
          "Your projects are not touched."),

        CondPageBreak(120 * mm),
        P("3. Element Count", h1),
        P("Element Count shows how many elements a view or a model contains, grouped by category. Use it to get a quick "
          "picture of what is in a model, or to bring those numbers into Excel. It only reads the model, so it "
          "cannot change anything and there is nothing to undo."),
        P("How to use it", h2),
        steps([
            "With a project open, click <b>PAC Tools › Model › Element Count</b>. The window opens and reads the active view.",
            "Choose <b>Active view</b> or <b>Whole model</b>. The table updates straight away.",
            "Carry on working in Revit if you like: the window can stay open. After you change the model or the "
            "view, press <b>Refresh</b>. The table does not update by itself.",
            "To take the numbers to Excel, press <b>Copy CSV</b> and paste, or press <b>Save CSV...</b> to save a file.",
        ]),
        Spacer(1, 8),
        CountWindow(),
        P("The Element Count window. This is a drawing with invented numbers, not a screenshot.", caption),
        CondPageBreak(60 * mm),
        P("The window", h2),
        table([
            ["Item", "What it does"],
            ["Active view", "Counts what belongs to the view currently open in Revit."],
            ["Whole model", "Counts every element in the project, in every view."],
            ["Elements / Share", "How many elements are in the category, and what percentage of the total that is. "
                                 "The biggest category comes first."],
            ["Refresh", "Reads the model again. While it waits for Revit the window shows <i>waiting for Revit...</i> "
                        "If Revit is busy, for example in the middle of a command, the read happens when you finish."],
            ["Copy CSV", "Puts the table on the clipboard, ready to paste into Excel."],
            ["Save CSV...", "Saves the table as a .csv file, with a file name suggested from the project and view."],
            ["Close", "Closes the window. It also closes by itself when you close the project."],
        ], [34 * mm, WIDTH - 34 * mm]),
        P("Reading the numbers", h2),
        bullets([
            "<b>Whole model counts more than you might expect.</b> It counts all instances, including non-modelled "
            "ones such as views, sheets, levels and annotation. Element types (family types) are never counted.",
            "<b>(no category)</b> is the row for elements Revit does not assign to any category.",
            "<b>Linked models are not counted.</b> A link counts as one element in the host model; "
            "the elements inside it are not included.",
            "The CSV has the columns <b>Category, Elements, Share</b>, and a <b>Total</b> row at the end.",
        ]),
        P("If you press the button when the window is already open, the open window comes to the front instead of a "
          "second one opening.", ParagraphStyle("t2", parent=body, spaceBefore=6)),

        P("4. Guide", h1),
        P("<b>PAC Tools › Help › Guide</b> opens this PDF in your PDF reader. The file is installed with the add-in, "
          "so it works without internet access."),

        P("5. About", h1),
        P("<b>PAC Tools › Help › About</b> shows the version you have and where the log file is. Quote the version "
          "number when you report a problem."),

        P("6. Planned: Excel to schedule", h1),
        callout("<b>Not available yet.</b> This section describes the plan, and details may change."),
        Spacer(1, 6),
        bullets([
            "<b>Import spreadsheet data.</b> Pick a workbook and worksheet, map the columns, and get a Revit schedule.",
            "<b>Refresh linked data.</b> The link to the workbook is remembered, so the schedule can be refreshed when the sheet changes.",
            "<b>Schedules from a selection.</b> A named table, a named range or a typed range becomes its own schedule.",
        ]),
        P("Planned formats are .xlsx, .xlsm, .xls (read-only) and .csv. Excel will not need to be installed.",
          ParagraphStyle("t3", parent=body, spaceBefore=6)),

        CondPageBreak(70 * mm),
        P("7. Troubleshooting", h1),
        table([
            ["What you see", "What to do"],
            ["No PAC Tools tab", "Check that you are in Revit 2025 or 2026 and that you restarted Revit after installing. "
                                 "If you chose not to load the add-in at the prompt, restart Revit and choose Always Load. "
                                 "Then check the log file below."],
            ["Installer: <i>Revit is running</i>", "Save your work, close every Revit window and run it again."],
            ["Installer: <i>.NET SDK is not installed</i>", "Install the .NET 8 SDK, close and reopen the window, and run it again."],
            ["Installer: <i>RevitAPI.dll was not found</i>", "Revit is not in the usual folder, or it is a different version. "
                                                            "The message lists the versions it found and the option to use."],
            ["<i>Open a Revit project first</i>", "Element Count needs an open project."],
            ["<i>works in project documents, not in the family editor</i>", "Switch to a project. Element Count does not run on families."],
            ["Red box: <i>the elements of the view cannot be counted</i>", "Some views, such as schedules, cannot be counted. "
                                                                           "Switch to a plan, section, elevation or 3D view, or choose Whole model."],
            ["Red box: <i>Windows would not give up the clipboard</i>", "Another program is holding the clipboard. Try again in a moment."],
            ["Guide says the file is missing", "Run the installer again. The PDF should sit next to " + code("PacTools.dll") + "."],
        ], [62 * mm, WIDTH - 62 * mm]),
        Spacer(1, 8),
        P("Where the files are", h2),
        table([
            ["File", "Location"],
            ["Log file", code("%LocalAppData%\\PACTools\\logs\\pactools.log")],
            ["Add-in", code("%AppData%\\Autodesk\\Revit\\Addins\\&lt;version&gt;\\PacTools\\")],
            ["Manifest", code("%AppData%\\Autodesk\\Revit\\Addins\\&lt;version&gt;\\PacTools.addin")],
        ], [30 * mm, WIDTH - 30 * mm]),
    ]
    return s


def build():
    doc = BaseDocTemplate(
        str(OUTPUT), pagesize=A4, title="PAC Tools - User guide", author="Tandon Consultants Pvt. Ltd.",
        subject="PAC Tools " + VERSION + " user guide",
    )
    first = Frame(MARGIN, 18 * mm, WIDTH, A4[1] - 66 * mm - 22 * mm, leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
    rest = Frame(MARGIN, 18 * mm, WIDTH, A4[1] - 18 * mm - 16 * mm, leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
    doc.addPageTemplates([
        PageTemplate(id="first", frames=[first], onPage=cover, autoNextPageTemplate="rest"),
        PageTemplate(id="rest", frames=[rest], onPage=footer),
    ])
    doc.build(story())
    print("wrote", OUTPUT, "version", VERSION)


if __name__ == "__main__":
    build()
