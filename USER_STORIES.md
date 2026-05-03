# Family Tree App — User Stories

**Tech Stack:** Blazor Server + ASP.NET Core + Entity Framework Core + SQLite (dev) / PostgreSQL (prod)
**Scope:** Single-user, single tree, no authentication required
**Relationship types:** Biological, Adoptive, Marriage, Stepparent

---

## Epic 1: Person / Individual Management

### US-001: Create a New Person
**As a** family tree user
**I want to** create a new person record with basic information
**So that** I can begin building or expanding the family tree

**Acceptance Criteria:**
- [ ] A "Add Person" form accepts: first name, last name, birth surname / maiden name (optional), birth date, birth place (optional, free-text e.g. "Springfield, IL"), death date (optional), death place (optional, free-text), gender, profile photo (optional), and free-text notes (optional)
- [ ] First name and last name are required fields; all others are optional
- [ ] Birth date must be a valid calendar date and, if a death date is also provided, must be earlier than the death date
- [ ] Gender options include at minimum: Male, Female, Non-binary, Unknown
- [ ] Submitting the form saves the person and redirects to their profile page
- [ ] A duplicate-warning is shown if a person with the same full name and birth date already exists

**Priority:** High

---

### US-002: View a Person's Profile
**As a** family tree user
**I want to** view a dedicated profile page for any person in the tree
**So that** I can see all stored information and their family connections at a glance

**Acceptance Criteria:**
- [ ] The profile page displays: full name (with birth surname shown as "(née [Birth Surname])" if recorded), birth date, birth place (if recorded), death date (if applicable), death place (if applicable), age, gender, photo, and notes
- [ ] The page lists all known relationships: biological parents, biological children, adoptive parents, adoptive children, and spouses/marriages
- [ ] Each listed relationship is a clickable link to that person's profile
- [ ] A "Deceased" badge is shown when a death date is present

**Priority:** High

---

### US-003: Edit a Person's Details
**As a** family tree user
**I want to** edit any field on an existing person's record
**So that** I can correct mistakes or add newly discovered information

**Acceptance Criteria:**
- [ ] An "Edit" button on the profile page opens an editable form pre-populated with existing data
- [ ] All fields editable in US-001 are editable here, including birth surname / maiden name
- [ ] Saving validates the same rules as creation (date ordering, required fields)
- [ ] A visual "unsaved changes" indicator is shown on the form while edits are pending
- [ ] A success notification confirms the save; validation errors are shown inline

**Priority:** High

---

### US-004: Delete a Person
**As a** family tree user
**I want to** delete a person from the tree
**So that** I can remove incorrectly added or duplicate records

**Acceptance Criteria:**
- [ ] A "Delete" action requires explicit confirmation (two-step modal)
- [ ] Deleting a person removes all parent-child relationship links (biological and adoptive) that reference them
- [ ] Deleting a person fully deletes any marriage records they are a party to — the marriage disappears from the other spouse's profile as well
- [ ] The warning modal lists both severed relationship links AND marriages that will be deleted before the user confirms
- [ ] Deletion is permanent; a success message confirms removal

**Priority:** High

---

### US-005: Upload and Manage a Profile Photo
**As a** family tree user
**I want to** upload a photo for a person
**So that** the tree is visually meaningful and easier to navigate

**Acceptance Criteria:**
- [ ] Accepted formats: JPEG, PNG, WebP; maximum file size: 5 MB
- [ ] A cropping/repositioning tool is shown after upload
- [ ] The photo is displayed as a thumbnail in the tree view and full size on the profile page
- [ ] A "Remove Photo" option restores the default avatar placeholder

**Priority:** Medium

---

### US-006: Add and Edit Notes on a Person
**As a** family tree user
**I want to** add free-text notes to a person's record
**So that** I can capture biographical details, sources, or stories that do not fit structured fields

**Acceptance Criteria:**
- [ ] The notes field supports plain text up to 5,000 characters
- [ ] Notes are displayed on the profile page below structured fields
- [ ] Notes can be edited or cleared independently
- [ ] Character count is shown as the user types

**Priority:** Medium

---

## Epic 2: Biological Parent-Child Relationships

### US-007: Add a Biological Parent to a Person
**As a** family tree user
**I want to** link an existing person as the biological parent of another person
**So that** direct lineage is accurately represented in the tree

**Acceptance Criteria:**
- [ ] "Add Biological Parent" opens a search-and-select dialog
- [ ] User can search by name or choose "Create new person" inline
- [ ] A person may have at most two biological parents
- [ ] Adding a biological parent automatically creates the reciprocal biological child relationship
- [ ] The system prevents adding a person as their own parent (circular reference check)
- [ ] Warning shown if prospective parent's birth date is after the child's birth date
- [ ] If the selected person is already recorded as a biological parent of this person, an error prevents saving a duplicate

**Priority:** High

---

### US-008: View Biological Parents on a Profile
**As a** family tree user
**I want to** see clearly labeled biological parents on a person's profile
**So that** I can distinguish them from adoptive parents at a glance

**Acceptance Criteria:**
- [ ] Biological parents are listed under a "Biological Parents" section with the label "(Biological)"
- [ ] Missing parent slots show "Unknown" with an "Add" prompt
- [ ] Each parent entry shows: name, birth year–death year, and a link to their profile

**Priority:** High

---

### US-009: Edit a Biological Parent Relationship
**As a** family tree user
**I want to** change which person is recorded as a biological parent
**So that** I can correct a wrongly assigned parent without deleting and re-adding records

**Acceptance Criteria:**
- [ ] An "Edit" icon next to each parent entry allows replacing the linked person
- [ ] Replacing updates reciprocal links on both old and new parent
- [ ] A confirmation dialog shows both old and new parent before saving

**Priority:** Medium

---

### US-010: Remove a Biological Parent Relationship
**As a** family tree user
**I want to** remove a biological parent link from a person's record
**So that** I can correct data without deleting either person

**Acceptance Criteria:**
- [ ] A "Remove" action requires confirmation
- [ ] Removing the link also removes the reciprocal child entry from the parent's record
- [ ] Neither person is deleted

**Priority:** High

---

### US-011: Add a Biological Child to a Person
**As a** family tree user
**I want to** link an existing person as a biological child
**So that** I can build the tree top-down when I know the parent first

**Acceptance Criteria:**
- [ ] "Add Biological Child" opens a search-and-select dialog
- [ ] Adding a child automatically sets that person as a biological parent of the child
- [ ] If the child already has two biological parents, the system warns before overwriting
- [ ] Circular reference check prevents adding an ancestor as a descendant

**Priority:** High

---

### US-012: View Biological Children on a Profile
**As a** family tree user
**I want to** see a list of biological children on a person's profile
**So that** I can quickly navigate to any child's record

**Acceptance Criteria:**
- [ ] Biological children listed under "Biological Children"
- [ ] Each entry shows: name, birth year, and a link to their profile
- [ ] List is sorted by birth date (ascending)
- [ ] "Add Biological Child" button is always visible in this section

**Priority:** High

---

### US-013: Remove a Biological Child Relationship
**As a** family tree user
**I want to** remove a biological child link from a person's record
**So that** I can correct a wrongly assigned child

**Acceptance Criteria:**
- [ ] "Remove" action requires confirmation
- [ ] Severs the reciprocal biological parent link on the child's record
- [ ] Neither person is deleted

**Priority:** High

---

### US-051: View Siblings on a Person's Profile
**As a** family tree user
**I want to** see a siblings section on a person's profile
**So that** I can quickly see who shares one or both biological parents with them

**Acceptance Criteria:**
- [ ] A "Siblings" section lists all people who share at least one biological parent with this person
- [ ] Full siblings (both biological parents in common) are shown first, labeled "(Full)"
- [ ] Half-siblings (exactly one biological parent in common) are shown next, labeled "(Half)"
- [ ] Each entry shows: name, shared parent(s), birth year, and a link to their profile
- [ ] List is sorted by birth date (ascending) within each group
- [ ] If no siblings exist, the section shows "No known siblings"

**Priority:** High

---

## Epic 3: Adoptive Parent-Child Relationships

### US-014: Add an Adoptive Parent to a Person
**As a** family tree user
**I want to** link an existing person as an adoptive parent and record the adoption date
**So that** the distinction between biological and adoptive relationships is preserved

**Acceptance Criteria:**
- [ ] "Add Adoptive Parent" opens a search-and-select dialog with an optional "Adoption Date" field
- [ ] No hard cap on the number of adoptive parents
- [ ] Relationship is labeled "(Adoptive)" everywhere it appears
- [ ] Adding the link creates the reciprocal adoptive child entry on the parent's record
- [ ] Circular reference check enforced
- [ ] If the selected person is already recorded as an adoptive parent of this person, an error prevents saving a duplicate

**Priority:** High

---

### US-015: View Adoptive Parents on a Profile
**As a** family tree user
**I want to** see clearly labeled adoptive parents on a person's profile
**So that** I can distinguish them from biological parents

**Acceptance Criteria:**
- [ ] Adoptive parents appear under "Adoptive Parents" labeled "(Adoptive)"
- [ ] Adoption date is shown next to each parent if recorded
- [ ] If no adoption date is recorded, shows "Date unknown"

**Priority:** High

---

### US-016: Edit an Adoptive Parent Relationship
**As a** family tree user
**I want to** update the adoption date or change the linked adoptive parent
**So that** I can correct details without removing and re-adding the relationship

**Acceptance Criteria:**
- [ ] "Edit" control opens a form with the adoptive parent name (changeable) and adoption date (editable)
- [ ] Changing the linked person updates reciprocal relationships accordingly

**Priority:** Medium

---

### US-017: Remove an Adoptive Parent Relationship
**As a** family tree user
**I want to** remove an adoptive parent link
**So that** I can correct wrongly assigned relationships without deleting records

**Acceptance Criteria:**
- [ ] "Remove" action requires confirmation
- [ ] Severs the reciprocal adoptive child entry on the parent's record
- [ ] Neither person is deleted

**Priority:** High

---

### US-018: Add an Adoptive Child to a Person
**As a** family tree user
**I want to** record that a person has adopted a child
**So that** I can build the adoptive family tree top-down

**Acceptance Criteria:**
- [ ] "Add Adoptive Child" opens a search-and-select dialog with an adoption date field
- [ ] Adding the link sets the reciprocal adoptive parent relationship on the child's record
- [ ] No upper limit on adoptive children per person
- [ ] Circular reference check enforced

**Priority:** High

---

### US-019: View Adoptive Children on a Profile
**As a** family tree user
**I want to** see a list of adoptive children on a person's profile
**So that** I can navigate to any adopted child's record and see when they were adopted

**Acceptance Criteria:**
- [ ] Adoptive children listed under "Adoptive Children" with "(Adoptive)" label
- [ ] Each entry shows: name, birth year, adoption date (or "Unknown"), and a link to profile
- [ ] List sorted by adoption date, then birth date

**Priority:** High

---

### US-020: Remove an Adoptive Child Relationship
**As a** family tree user
**I want to** remove an adoptive child link
**So that** I can correct wrongly assigned relationships

**Acceptance Criteria:**
- [ ] "Remove" action requires confirmation
- [ ] Severs the reciprocal adoptive parent entry on the child's record
- [ ] Neither person is deleted

**Priority:** High

---

## Epic 4: Marriage Management

### US-021: Add a Marriage Between Two People
**As a** family tree user
**I want to** record a marriage between two people with a start date
**So that** spousal relationships and family units are captured in the tree

**Acceptance Criteria:**
- [ ] "Add Marriage" form includes: spouse (search-and-select), marriage start date (required), marriage location (optional, free-text), end date (optional), end reason (optional: Divorce, Death of spouse, Annulment, Separation, Unknown)
- [ ] Marriage appears on both spouses' profiles
- [ ] A person may have multiple marriages
- [ ] If end reason is "Death of spouse," the system suggests auto-populating end date from spouse's death date
- [ ] Cannot marry themselves (self-reference check)
- [ ] If an active (non-ended) marriage between the same two people already exists, an error prevents saving a duplicate

**Priority:** High

---

### US-022: View Marriages on a Profile
**As a** family tree user
**I want to** see all marriages listed on a person's profile
**So that** I can understand their marital history and navigate to spouses' profiles

**Acceptance Criteria:**
- [ ] Marriages appear under "Marriages / Partnerships"
- [ ] Each entry shows: spouse name (linked), start date, end date (or "Ongoing"), end reason
- [ ] Sorted chronologically by start date
- [ ] Current/ongoing marriage is visually distinguished

**Priority:** High

---

### US-023: Edit a Marriage Record
**As a** family tree user
**I want to** edit the details of an existing marriage
**So that** I can add missing dates or correct errors

**Acceptance Criteria:**
- [ ] "Edit" control opens the same form as US-021, pre-populated
- [ ] All fields editable (start date, end date, end reason, linked spouse)
- [ ] Changing spouse updates both old and new spouse's profiles
- [ ] End date must be on or after start date

**Priority:** Medium

---

### US-024: Remove a Marriage Record
**As a** family tree user
**I want to** delete a marriage record
**So that** I can correct wrongly linked spouses without deleting either person

**Acceptance Criteria:**
- [ ] "Remove" requires confirmation
- [ ] Removing removes the marriage from both spouses' profiles
- [ ] Neither person is deleted

**Priority:** High

---

### US-025: Record a Divorce
**As a** family tree user
**I want to** mark a marriage as ended by divorce with an end date
**So that** the marital timeline is historically accurate

**Acceptance Criteria:**
- [ ] Selecting "Divorce" as end reason with an end date saves correctly
- [ ] Entry shows: "[Start date] – [End date] (Divorced)" on both profiles
- [ ] Divorce date must be on or after the marriage start date

**Priority:** High

---

### US-026: Record Widowhood (Marriage Ended by Death)
**As a** family tree user
**I want to** mark a marriage as ended by the death of a spouse
**So that** the family record reflects both the marriage and the loss accurately

**Acceptance Criteria:**
- [ ] Selecting "Death of spouse" triggers an offer to auto-fill end date from deceased spouse's death date
- [ ] Entry shows: "[Start date] – [End date] (Widowed)"
- [ ] If spouse's death date is later updated, user is notified the marriage end date may need review

**Priority:** High

---

### US-027: Record an Annulment
**As a** family tree user
**I want to** mark a marriage as annulled
**So that** the legal and historical distinction from divorce is preserved

**Acceptance Criteria:**
- [ ] "Annulment" is a selectable end reason
- [ ] Entry shows: "[Start date] – [End date] (Annulled)"
- [ ] All date validation rules from US-021 apply

**Priority:** Medium

---

## Epic 5: Family Tree Visualization

### US-028: View the Full Family Tree
**As a** family tree user
**I want to** see a graphical tree diagram of all connected family members
**So that** I can understand the overall structure and navigate visually

**Acceptance Criteria:**
- [ ] Tree renders all persons as nodes with connecting edges
- [ ] Biological edges are visually distinct from adoptive edges (solid vs. dashed line)
- [ ] Marriage connections shown with a horizontal link between spouses
- [ ] Nodes display photo/avatar, full name, and birth/death years
- [ ] Diagram is pan-and-zoom capable
- [ ] "Fit to screen" button resets the view

**Priority:** High

---

### US-029: Focus the Tree on a Specific Person
**As a** family tree user
**I want to** center the tree view on a selected individual
**So that** I can explore their immediate family without being overwhelmed

**Acceptance Criteria:**
- [ ] Clicking a node (or "Focus on this person" on profile) re-centers the view
- [ ] Focused view shows: the person, parents (up one generation), children (down one generation), and spouses
- [ ] Expand icons on edges allow loading further generations
- [ ] Breadcrumb trail shows navigation path

**Priority:** High

---

### US-030: Navigate Between Generations
**As a** family tree user
**I want to** move up or down the generational hierarchy from any node
**So that** I can explore ancestors and descendants without losing context

**Acceptance Criteria:**
- [ ] "Go to parents" and "Go to children" controls available from tree node and profile
- [ ] Moving up/down shifts the focused generation with smooth animation
- [ ] Current person's node remains visually highlighted
- [ ] Browser back/forward buttons return to previous focused node

**Priority:** High

---

### US-031: Distinguish Relationship Types Visually
**As a** family tree user
**I want to** see different visual styles for biological, adoptive, and marriage relationships
**So that** I can instantly understand the nature of each connection

**Acceptance Criteria:**
- [ ] Biological parent-child edges: solid line
- [ ] Adoptive parent-child edges: dashed line with "(A)" label
- [ ] Marriage connections: double horizontal line or special symbol between spouses
- [ ] A legend explaining each line style is accessible on the tree view

**Priority:** High

---

### US-032: View a Pedigree (Ancestor) Chart
**As a** family tree user
**I want to** view an ancestor-focused pedigree chart for a selected person
**So that** I can trace lineage back through multiple generations in a compact layout

**Acceptance Criteria:**
- [ ] "Pedigree View" shows the selected person on the left branching out to parents, grandparents, etc.
- [ ] Supports at least 4 generations
- [ ] Each box shows: name, birth year, death year
- [ ] Clicking a box navigates to that person's profile

**Priority:** Medium

---

### US-033: View a Descendant Chart
**As a** family tree user
**I want to** view a descendant chart from a selected ancestor
**So that** I can see all children, grandchildren, and further descendants in one view

**Acceptance Criteria:**
- [ ] "Descendants View" shows the selected person at the top branching downward
- [ ] All biological and adoptive children shown with relationship type labeled
- [ ] Supports at least 4 generations
- [ ] Clicking a box navigates to that person's profile

**Priority:** Medium

---

## Epic 6: Search and Navigation

### US-034: Search for a Person by Name
**As a** family tree user
**I want to** search for any person in the tree by name
**So that** I can quickly locate a record without manually navigating the tree

**Acceptance Criteria:**
- [ ] Persistent search bar accessible from all pages
- [ ] Search triggered on keystroke (debounced), returns results as user types
- [ ] Matches on first name, last name, or full name (case-insensitive, partial match)
- [ ] Each result shows: photo thumbnail, full name, birth year, death year
- [ ] Clicking a result navigates to that person's profile
- [ ] If no results: "No match found — Add a new person?" prompt

**Priority:** High

---

### US-035: Search for a Person by Birth or Death Date
**As a** family tree user
**I want to** filter people by birth year or death year
**So that** I can find people when I know a date but not the exact name

**Acceptance Criteria:**
- [ ] "Advanced Search" panel exposes optional birth year range and death year range filters
- [ ] Filters can be combined with name search
- [ ] Results update immediately when filter values change
- [ ] Result count shown ("Showing 5 of 132 people")

**Priority:** Medium

---

### US-036: Browse All People in the Tree
**As a** family tree user
**I want to** view a paginated list of all people in the tree
**So that** I can audit records and spot duplicates or missing data

**Acceptance Criteria:**
- [ ] "People" list page shows all records in a sortable table or card grid
- [ ] Sortable columns: last name, first name, birth date, death date
- [ ] Each row/card links to the person's profile
- [ ] Pagination or infinite scroll handles large trees

**Priority:** Medium

---

### US-052: Empty State — First-Time Use
**As a** new user
**I want to** see a helpful prompt when the tree has no people yet
**So that** I know how to get started without confusion

**Acceptance Criteria:**
- [ ] When no people exist, the tree view shows a centered "Get started" prompt with an "Add your first person" button
- [ ] The people list (US-036) shows the same prompt instead of an empty table or grid
- [ ] Search (US-034) returns a "No people in the tree yet — Add someone?" prompt when the tree is empty
- [ ] Clicking any of these prompts opens the Add Person form (US-001)

**Priority:** High

---

## Epic 7: Edge Cases and Complex Relationships

### US-037: Represent Half-Siblings via a Shared Biological Parent
**As a** family tree user
**I want to** add two people who share only one biological parent
**So that** half-sibling relationships are correctly represented

**Acceptance Criteria:**
- [ ] System allows two people to share exactly one biological parent with different other parents
- [ ] Half-siblings listed under "Siblings" with a "(Half)" qualifier
- [ ] Tree diagram connects half-siblings through their shared parent, not directly

**Priority:** High

---

### US-038: Handle Stepparent Relationships
**As a** family tree user
**I want to** indicate that a person's parent's spouse is their stepparent
**So that** blended family structures are clearly represented

**Acceptance Criteria:**
- [ ] On a child's profile, a "Label as Stepparent" action is available next to any of the child's biological/adoptive parent's current spouses
- [ ] The stepparent label is applied manually by the user — it is not inferred automatically
- [ ] Stepparent is distinct from biological and adoptive — does not appear in those sections
- [ ] "Stepchildren" section shown on the stepparent's profile
- [ ] Stepparent connection shown in tree with a distinct edge type
- [ ] Removing the underlying marriage record also removes the stepparent label

**Priority:** Medium

---

### US-039: Allow a Person to Have Both Biological and Adoptive Parents
**As a** family tree user
**I want to** record both biological and adoptive parents for the same person
**So that** complex adoption histories are fully preserved

**Acceptance Criteria:**
- [ ] A person's profile can simultaneously list up to two biological parents AND one or more adoptive parents
- [ ] Both sections shown concurrently with clear labels
- [ ] Tree view shows both connection types with respective visual styles

**Priority:** High

---

### US-040: Prevent and Detect Circular Relationships
**As a** family tree user
**I want to** be warned if I am about to create an impossible ancestral loop
**So that** data integrity is maintained

**Acceptance Criteria:**
- [ ] Before saving any parent-child or adoptive link, system checks the prospective parent is not already a descendant of the child
- [ ] If loop detected, save is blocked with a clear error (e.g., "Cannot add Alex as a parent of Sam — Alex is already a descendant of Sam")
- [ ] Check applies to both biological and adoptive relationships

**Priority:** High

---

### US-041: Record a Person with Unknown Parents
**As a** family tree user
**I want to** add a person to the tree without knowing either parent
**So that** I can document individuals even when lineage is partially or wholly unknown

**Acceptance Criteria:**
- [ ] Creating a person with no parents is valid and saves without warnings
- [ ] Missing parent slots show "Unknown" — not treated as errors
- [ ] The person appears as a root node in the tree view

**Priority:** High

---

### US-042: Handle Remarriage and Multiple Marriages Correctly
**As a** family tree user
**I want to** add multiple sequential marriages to a person
**So that** individuals who married more than once are accurately represented

**Acceptance Criteria:**
- [ ] A person may have any number of marriage records
- [ ] Marriages sorted chronologically in the "Marriages" section
- [ ] Overlapping date ranges trigger a warning but user can still save
- [ ] Children from different marriages can each be linked to their respective parents independently

**Priority:** High

---

### US-044: Represent Same-Sex Partnerships and Marriages
**As a** family tree user
**I want to** record marriages and partnerships between any two people regardless of gender
**So that** all family structures are supported without bias

**Acceptance Criteria:**
- [ ] Marriage form imposes no gender constraint on either spouse
- [ ] All marriage-related displays use gender-neutral language ("Spouse" not "Husband/Wife")
- [ ] Adoptive children of same-sex couples linked to both parents using adoptive relationship type

**Priority:** High

---

### US-045: Record a Deceased Person with an Approximate or Partial Date
**As a** family tree user
**I want to** enter approximate or partial dates (e.g., "circa 1880" or just a year)
**So that** I can document historical ancestors even when exact dates are unknown

**Acceptance Criteria:**
- [ ] Date fields accept: full date (YYYY-MM-DD), year and month (YYYY-MM), year only (YYYY), and a "circa" flag
- [ ] Dates with "circa" flag displayed with "c." prefix (e.g., "c. 1880")
- [ ] Age calculations using approximate dates display a "~" prefix
- [ ] Partial dates sort correctly

**Priority:** Medium

---

## Epic 8: Data Integrity and Usability

### US-046: Undo the Last Relationship Change
**As a** family tree user
**I want to** undo the most recent relationship addition or removal
**So that** an accidental change can be reversed without manually recreating the link

**Acceptance Criteria:**
- [ ] Globally accessible "Undo" button reverses the most recent relationship change
- [ ] Undoable actions: adding or removing a parent, child, adoptive link, or marriage
- [ ] Only the single most recent action is undoable — there is no multi-step history or redo
- [ ] Person creation, edits, and deletion are out of scope for undo

**Priority:** Medium

---

### US-048: Export the Family Tree Data
**As a** family tree user
**I want to** export all family tree data to a standard format
**So that** I can back up my data or import it into another genealogy application

**Acceptance Criteria:**
- [ ] Export available as GEDCOM (.ged) and/or JSON formats
- [ ] All persons, relationships (biological, adoptive, marriage), dates, and notes included
- [ ] Photos included in a ZIP archive alongside the data file
- [ ] Download link provided immediately after export completes

**Priority:** Medium

---

### US-049: Import Family Tree Data
**As a** family tree user
**I want to** import a GEDCOM or JSON file to populate the tree
**So that** I can migrate existing genealogy data without manual re-entry

**Acceptance Criteria:**
- [ ] Import screen accepts a GEDCOM (.ged) or JSON file
- [ ] Preview lists number of persons, relationships, and potential conflicts before finalizing
- [ ] Conflict resolution options: Skip, Overwrite, or Merge
- [ ] Progress indicator shown during import
- [ ] Import summary report lists successes, skips, and errors

**Priority:** Medium

---

### US-050: Print or Generate a PDF of the Family Tree
**As a** family tree user
**I want to** generate a printable PDF of the family tree diagram or a person's profile
**So that** I can share the information in physical or document form

**Acceptance Criteria:**
- [ ] "Print / Export PDF" option available from tree view and individual profile pages
- [ ] Tree PDF renders the currently visible portion in a readable layout
- [ ] Profile PDF includes: all personal details, photo (if present), and formatted list of all relationships
- [ ] Page size options: A4 and Letter
- [ ] File named after the person or tree (e.g., "SmithFamily-Tree.pdf")

**Priority:** Low

---

## Summary Table

| Story ID | Title | Epic | Priority |
|----------|------------------------------------------------|-------------------------------|----------|
| US-001 | Create a New Person | Person Management | High |
| US-002 | View a Person's Profile | Person Management | High |
| US-003 | Edit a Person's Details | Person Management | High |
| US-004 | Delete a Person | Person Management | High |
| US-005 | Upload and Manage a Profile Photo | Person Management | Medium |
| US-006 | Add and Edit Notes on a Person | Person Management | Medium |
| US-007 | Add a Biological Parent | Biological Relationships | High |
| US-008 | View Biological Parents on a Profile | Biological Relationships | High |
| US-009 | Edit a Biological Parent Relationship | Biological Relationships | Medium |
| US-010 | Remove a Biological Parent Relationship | Biological Relationships | High |
| US-011 | Add a Biological Child | Biological Relationships | High |
| US-012 | View Biological Children on a Profile | Biological Relationships | High |
| US-013 | Remove a Biological Child Relationship | Biological Relationships | High |
| US-014 | Add an Adoptive Parent | Adoptive Relationships | High |
| US-015 | View Adoptive Parents on a Profile | Adoptive Relationships | High |
| US-016 | Edit an Adoptive Parent Relationship | Adoptive Relationships | Medium |
| US-017 | Remove an Adoptive Parent Relationship | Adoptive Relationships | High |
| US-018 | Add an Adoptive Child | Adoptive Relationships | High |
| US-019 | View Adoptive Children on a Profile | Adoptive Relationships | High |
| US-020 | Remove an Adoptive Child Relationship | Adoptive Relationships | High |
| US-021 | Add a Marriage Between Two People | Marriage Management | High |
| US-022 | View Marriages on a Profile | Marriage Management | High |
| US-023 | Edit a Marriage Record | Marriage Management | Medium |
| US-024 | Remove a Marriage Record | Marriage Management | High |
| US-025 | Record a Divorce | Marriage Management | High |
| US-026 | Record Widowhood | Marriage Management | High |
| US-027 | Record an Annulment | Marriage Management | Medium |
| US-028 | View the Full Family Tree | Visualization | High |
| US-029 | Focus the Tree on a Specific Person | Visualization | High |
| US-030 | Navigate Between Generations | Visualization | High |
| US-031 | Distinguish Relationship Types Visually | Visualization | High |
| US-032 | View a Pedigree (Ancestor) Chart | Visualization | Medium |
| US-033 | View a Descendant Chart | Visualization | Medium |
| US-034 | Search for a Person by Name | Search and Navigation | High |
| US-035 | Search by Birth or Death Date | Search and Navigation | Medium |
| US-036 | Browse All People in the Tree | Search and Navigation | Medium |
| US-037 | Half-Siblings via Shared Biological Parent | Edge Cases | High |
| US-038 | Handle Stepparent Relationships | Edge Cases | Medium |
| US-039 | Biological and Adoptive Parents Together | Edge Cases | High |
| US-040 | Prevent Circular Relationships | Edge Cases | High |
| US-041 | Person with Unknown Parents | Edge Cases | High |
| US-042 | Multiple Sequential Marriages | Edge Cases | High |
| US-044 | Same-Sex Partnerships and Marriages | Edge Cases | High |
| US-045 | Approximate or Partial Dates | Edge Cases | Medium |
| US-046 | Undo Last Relationship Change | Data Integrity | Medium |
| US-048 | Export Family Tree Data | Data Integrity | Medium |
| US-049 | Import Family Tree Data | Data Integrity | Medium |
| US-050 | Print or Generate a PDF | Data Integrity | Low |
| US-051 | View Siblings on a Profile | Biological Relationships | High |
| US-052 | Empty State — First-Time Use | Search and Navigation | High |
