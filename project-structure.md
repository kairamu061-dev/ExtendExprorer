# Project Structure

## Directory Layout

```
docs/
├── index.md               # Required — list of feature areas and sub-items
├── project_overview.md    # Required — overall project overview
├── glossary.md            # Required — domain-specific terminology
├── tags.md                # Required — inline tag definitions and schema
└── {feature-area}/        # One directory per feature area
    ├── overview.md
    ├── spec.md
    ├── design.md
    ├── tasks.md
    ├── test-cases.md
    ├── dev-notes.md
    └── {sub-item}/        # Created as needed per split rules
        └── ...

templates/                 # Document templates for feature areas
├── overview.md
├── spec.md
├── design.md
├── tasks.md
└── dev-notes.md

issues/                    # Bug ticket management
├── index.md               # Ticket list and state legend
├── templates/
│   └── BUG-template.md
└── tickets/
    └── BUG-{NNN}.md        # Added sequentially as bugs are reported
```

---

## Document Templates

### project_overview.md
- **Purpose & Background**: The problem this project solves and why it exists
- **Scope**: What will and will not be built
- **Tech Stack Overview**: Technologies used and rationale
- **Overall Architecture**: High-level system structure
- **Constraints**: Technical, environmental, and resource limitations

### overview.md (per feature area)
- **Purpose & Background**: Why this feature is needed
- **Scope**: What will and will not be built
- **Constraints**: Technical and resource limitations
- **Definition of Done**: What constitutes completion

### spec.md
- **Feature List**: Enumeration of provided features
- **Screens & User Flow**: Step-by-step user interactions
- **Screen / State Details**: Display content, actions, and transitions
- **Error Cases**: Behavior for abnormal conditions
- **Out of Scope**: Things intentionally not handled

### design.md
- **Tech Selection**: Technologies used and rationale
- **Architecture**: Component structure
- **Data Structures**: Key data models and schemas
- **Interfaces**: API and function interfaces
- **Dependencies**: External libraries and services

### tasks.md
- **Task List**: Checkbox format
- **Dependencies**: Ordering constraints between tasks
- **Status**: Todo / In progress / Done

### dev-notes.md
- **Implementation Decisions**: Why a particular approach was chosen
- **Issues & Resolutions**: Problems encountered and how they were solved
- **Deviations from Design**: Differences from the design document and reasons
- **Future Work**: Current limitations and items to address later
- **Requests to User**: Recorded when skills, permissions, or information are lacking

### Bug ticket (issues/tickets/BUG-{NNN}.md)

Created following `issues/templates/BUG-template.md`.
