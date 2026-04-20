# Design System Direction

## Figma

- **Archivo:** https://www.figma.com/design/cFYBwjPLqAArvgg04DJLmp/Gestion-de-Caja?node-id=0-1&t=9i5CicNtw4gPKe3z-1
- **File key:** `cFYBwjPLqAArvgg04DJLmp`

## Core Take

This project should feel like a premium finance dashboard with calm authority.
The visual target is not "creative SaaS" and not "generic admin template".
It should look:

- clear
- controlled
- premium
- data-heavy without feeling crowded
- soft in surface treatment but strict in layout

The closest direction is:

- `ChatFin` for premium structure and restraint
- `SaaS Dashboard` for dashboard hierarchy and card composition
- `Personal Finance` for softness and warmth
- `SnowUI` only for variant coverage, not as the main aesthetic source

Target blend:

- `70%` ChatFin + SaaS Dashboard
- `20%` Personal Finance
- `10%` SnowUI

If the UI ever starts looking like a random Tailwind admin starter, it is off-track.

## Experience Principles

### 1. Clarity beats decoration

Every block should answer one question fast.
No card should try to be a KPI, a chart, and a filter panel at the same time.

### 2. Hierarchy comes from scale, not noise

Use size, weight, spacing, and position before using color.
Color is a support system, not the main structure.

### 3. Dense information, low visual pressure

The interface should carry a lot of content while still feeling quiet.
That means:

- large outer spacing
- generous card padding
- subtle borders
- soft tinted surfaces
- minimal iconography

### 4. Numbers are the hero

The most visually dominant element on most screens should be the main business number.
Not the icon. Not the gradient. Not the chart fill.

### 5. Charts should feel designed, not "BI software"

No harsh gridlines.
No loud legends.
No saturated rainbow palettes.
No default chart-library look.

### 6. Motion should confirm state, not ask for attention

Animations must make navigation, filtering, and data updates feel deliberate.
If the motion starts to feel showy, it is wrong.

## Personality

The system should communicate:

- competence
- reliability
- precision
- financial trust
- modern product maturity

It should not communicate:

- crypto hype
- startup gimmicks
- enterprise deadness
- template-marketplace blandness

Keywords:

- calm
- premium
- measured
- breathable
- data-first
- soft-edged
- quietly technical

## Color System

### Color Philosophy

Most of the interface must be built from neutrals.
Accent colors exist to mark state, guide focus, and support charts.
If more than a few elements are shouting with color, the palette is being misused.

Recommended distribution:

- `80%` neutrals
- `15%` primary accent
- `5%` semantic and chart accents

### Base Palette

#### Neutrals

- `--bg-app: #F5F8FC`
- `--bg-canvas: #F8FAFD`
- `--bg-surface: #FFFFFF`
- `--bg-surface-soft: #F2F6FC`
- `--bg-surface-muted: #EEF3FA`
- `--border-soft: rgba(23, 33, 52, 0.08)`
- `--border-strong: rgba(23, 33, 52, 0.14)`
- `--text-primary: #1E2430`
- `--text-secondary: #6E7787`
- `--text-muted: #98A2B3`
- `--text-inverse: #FFFFFF`

#### Brand Accent

Primary accent should live in the blue family with enough maturity to feel financial.

- `--accent-primary: #4C7DFF`
- `--accent-primary-hover: #3F6FEF`
- `--accent-primary-soft: #EAF1FF`
- `--accent-primary-soft-2: #DCE8FF`

#### Secondary Accent Options

Use sparingly. These are support accents, not equal peers.

- `--accent-teal: #58C7C2`
- `--accent-teal-soft: #E8FAF8`
- `--accent-indigo: #6C72FF`
- `--accent-indigo-soft: #ECEEFF`
- `--accent-lilac: #9B8CFF`
- `--accent-lilac-soft: #F1EEFF`

#### Semantic

- `--success: #2FB36D`
- `--success-soft: #EAF8F0`
- `--warning: #F5A524`
- `--warning-soft: #FFF4DF`
- `--danger: #E05666`
- `--danger-soft: #FDECEF`

### Background Usage Rules

- App background should never be pure white.
- Main shell can be white.
- Secondary zones may use a cool tint.
- Avoid strong colored backgrounds behind content except in contained promotional or onboarding sections.

### Chart Colors

Default chart palette:

- line 1: `#4C7DFF`
- line 2: `#58C7C2`
- line 3: `#9B8CFF`
- bar positive: `#72D5A3`
- bar neutral: `#BED2FF`
- bar negative: `#F29AA5`

Rules:

- max `3-4` visible hues per chart group
- use opacity for variation before adding new colors
- chart fills should stay soft and translucent

## Typography

### Font Direction

Use a clean neutral sans.
Best options:

- `Inter`
- `Geist`
- `Manrope`
- `SF Pro Display` if available in native contexts

Recommended default:

- `Inter` for implementation simplicity

Do not use:

- decorative display fonts
- geometric fonts that feel too branding-heavy
- condensed fonts
- anything with too much personality

### Typographic Role System

#### Display / Page Title

- `32px`
- `700`
- line-height `1.15`
- letter-spacing `-0.02em`

#### Section Title

- `20px`
- `600`
- line-height `1.25`

#### Card Title

- `14px`
- `600`
- line-height `1.3`

#### KPI Hero

- `36px`
- `700` or `800`
- line-height `1.05`
- letter-spacing `-0.03em`

#### KPI Secondary

- `24px`
- `700`
- line-height `1.1`

#### Body

- `14px`
- `500`
- line-height `1.45`

#### Small Meta

- `12px`
- `500`
- line-height `1.4`

#### Data Table

- `13px`
- `500`
- line-height `1.4`

### Typography Rules

- Big numbers should visually anchor the card.
- Labels should be quiet and short.
- Secondary text must stay cool and low-contrast.
- Never use more than `3` font weights on one screen unless there is a strong reason.

## Layout System

### Structural Model

The ideal screen shell:

- outer app frame with rounded corners
- left sidebar or rail
- top utility row
- modular card grid

### Spacing Scale

Use an `8px` base system:

- `4`
- `8`
- `12`
- `16`
- `20`
- `24`
- `32`
- `40`
- `48`

Recommended usage:

- page gutters: `24-32`
- card padding: `20-24`
- grid gaps: `16-24`
- section separation: `32-40`

### Radius Scale

Rounded corners are a major part of the look.
Not cartoonish, but definitely soft.

- small controls: `10-12px`
- cards: `18-24px`
- app shell: `28-32px`
- pills: `999px`

### Grid Rules

Desktop:

- `12-column` logical grid
- card widths should align to clear spans
- avoid awkward half-column chaos

Tablet:

- collapse into `6` columns

Mobile:

- single-column stack
- keep KPI blocks visually strong
- charts may simplify or reduce detail

### Composition Rules

- Keep one large hero zone per page.
- Balance charts with tables or lists.
- Avoid same-size card repetition across the full screen.
- Use one dominant row and one support row when possible.

Bad:

- four identical KPI cards
- four identical chart cards
- equally weighted everything

Good:

- hero KPI + support KPI + chart + activity feed
- large analytic area balanced by narrow side column

## Sidebar System

### Sidebar Character

The sidebar should feel calm and architectural.
It is not a dark developer panel and not a giant colored marketing slab.

Rules:

- light surface
- subtle separators
- small line icons
- active item shown with soft filled pill
- profile zone quiet and compact

### Navigation States

Default item:

- icon + label
- low contrast

Hover:

- background tint appears
- text darkens slightly

Active:

- light tinted pill
- icon and label increase contrast
- optional small side marker or dot

## Surfaces and Elevation

### Surface Treatment

Cards should feel:

- soft
- crisp
- slightly lifted
- not glossy

Use:

- white or barely tinted backgrounds
- hairline borders
- very soft shadows

Avoid:

- frosted glass everywhere
- blur-heavy overlays
- hard drop shadows
- dark noisy overlays in light mode

### Suggested Shadows

Primary card shadow:

- `0 8px 24px rgba(16, 24, 40, 0.06)`

Hover card shadow:

- `0 12px 28px rgba(16, 24, 40, 0.08)`

Shell shadow:

- `0 24px 60px rgba(16, 24, 40, 0.10)`

## Component Rules

### KPI Cards

Structure:

- small label
- large numeric value
- optional delta row
- optional sparkline or icon

Rules:

- one main number
- one supporting state
- no clutter under the KPI

### Chart Cards

Structure:

- title row
- small context filter or period selector
- chart area
- optional compact legend

Rules:

- chart area must have breathing room
- legends stay compact
- labels stay small

### Tables / Lists

Rules:

- minimal separators
- generous row height
- no strong zebra striping
- right-align numeric columns when useful
- status indicators should be color-backed but soft

### Activity Feed

The right-column feed in these references is useful.
It adds life without dominating the layout.

Rules:

- avatar first
- message concise
- timestamp quiet
- rows compact but airy

### Search

Use pill-shaped or softly rounded search fields.
Very light fill.
Embedded icon.
No heavy outline.

### Buttons

Primary:

- medium blue fill
- white text
- rounded `12-14px`
- height `36-40px`

Secondary:

- white or soft-tint fill
- subtle border
- dark text

Ghost:

- transparent
- used for utilities only

## Iconography

Use simple line icons.

Rules:

- `1.75px` to `2px` stroke feel
- compact
- neutral by default
- active through background and contrast, not icon replacement

Avoid:

- colorful icon packs
- heavy solid icons everywhere
- mismatched icon weights

## Charts and Data Visualization

### Line Charts

- stroke `2-3px`
- smooth curve
- one or two key points highlighted at most
- optional faint area fill

### Bar Charts

- rounded bars
- soft axis labels
- strong contrast between active and inactive series

### Donut Charts

- thick enough to read
- no more than `4` segments
- center value allowed when useful

### Gridlines

- extremely subtle
- use cool gray with low opacity

### Axes and Labels

- tiny
- neutral
- never darker than secondary text

## Motion System

## Motion Philosophy

Motion should make the UI feel more expensive and more legible.
Not more playful.

### Timing

- hover: `160-180ms`
- tabs / filters / button state: `180-240ms`
- panel transitions: `220-280ms`
- chart reveal: `400-700ms`
- count-up metrics: `500-800ms`

### Easing

Preferred:

- `cubic-bezier(0.22, 1, 0.36, 1)`
- standard ease-out curves

Avoid:

- bouncy springs on dashboard chrome
- exaggerated elastic movement

### Motion Patterns

#### Hover

- `translateY(-2px to -4px)`
- tiny shadow increase
- border becomes slightly clearer

#### Tab Switch

- content fade from `0` to `1`
- subtle horizontal shift `6px to 12px`

#### Number Update

- count-up animation
- optional soft flash on delta badge

#### Chart Load

- lines reveal left-to-right
- bars rise from baseline with slight stagger
- donut draws via stroke offset

#### Sidebar Active Change

- active pill glides
- icon state changes smoothly

### Motion Rule

At any given time, there should only be one obvious movement event competing for attention.

## Dark Mode

Dark mode is optional but allowed.
If implemented, it should follow `SnowUI` only at the structural level, not stylistically.

Rules:

- keep contrast high enough for finance data
- use deep charcoal, not true black
- preserve the same spacing and softness
- use muted chart glows only if extremely restrained

Suggested dark tokens:

- `--bg-app-dark: #0F141C`
- `--bg-surface-dark: #151C26`
- `--bg-surface-soft-dark: #1A2230`
- `--border-dark: rgba(255,255,255,0.08)`
- `--text-primary-dark: #F2F5FA`
- `--text-secondary-dark: #A7B0C0`

## Anti-Patterns

If you see any of this, kill it:

- saturated gradients across major UI surfaces
- multiple accent colors fighting in the same row
- oversized icons competing with metrics
- generic Tailwind card soup
- strong shadows under every block
- pure black text on pure white surfaces
- charts with default library styling
- too many pills, chips, toggles, and badges in one card
- every card having equal visual priority
- motion that bounces

## Implementation Tokens

Use this as the starting token layer.

```css
:root {
  --bg-app: #F5F8FC;
  --bg-canvas: #F8FAFD;
  --bg-surface: #FFFFFF;
  --bg-surface-soft: #F2F6FC;
  --bg-surface-muted: #EEF3FA;

  --border-soft: rgba(23, 33, 52, 0.08);
  --border-strong: rgba(23, 33, 52, 0.14);

  --text-primary: #1E2430;
  --text-secondary: #6E7787;
  --text-muted: #98A2B3;
  --text-inverse: #FFFFFF;

  --accent-primary: #4C7DFF;
  --accent-primary-hover: #3F6FEF;
  --accent-primary-soft: #EAF1FF;
  --accent-primary-soft-2: #DCE8FF;

  --accent-teal: #58C7C2;
  --accent-teal-soft: #E8FAF8;
  --accent-indigo: #6C72FF;
  --accent-indigo-soft: #ECEEFF;
  --accent-lilac: #9B8CFF;
  --accent-lilac-soft: #F1EEFF;

  --success: #2FB36D;
  --success-soft: #EAF8F0;
  --warning: #F5A524;
  --warning-soft: #FFF4DF;
  --danger: #E05666;
  --danger-soft: #FDECEF;

  --radius-sm: 12px;
  --radius-md: 18px;
  --radius-lg: 24px;
  --radius-xl: 32px;
  --radius-pill: 999px;

  --shadow-card: 0 8px 24px rgba(16, 24, 40, 0.06);
  --shadow-card-hover: 0 12px 28px rgba(16, 24, 40, 0.08);
  --shadow-shell: 0 24px 60px rgba(16, 24, 40, 0.10);

  --ease-premium: cubic-bezier(0.22, 1, 0.36, 1);
  --duration-fast: 180ms;
  --duration-base: 240ms;
  --duration-slow: 420ms;
}
```

## Screen Recipe

Use this default page recipe for major dashboard screens:

1. Outer app shell with large radius and restrained shadow.
2. Left sidebar with soft active pill.
3. Top row with page title, search, and compact utilities.
4. Hero analytics row:
   - one dominant KPI or chart card
   - one or two supporting metric cards
5. Secondary row:
   - transaction table or list
   - chart or category breakdown
   - optional activity column
6. Tertiary utilities only if needed.

## Build Checklist

- Is the primary information obvious within `2 seconds`?
- Is the page mostly neutral with only a few color accents?
- Are card sizes intentionally varied?
- Are numbers more visually important than icons?
- Do charts look custom rather than library-default?
- Are borders and shadows subtle?
- Does motion feel premium and restrained?
- Would this still look good if the accent color were removed?

If the answer to the last question is no, the design is too dependent on color and too weak in structure.

## Reference Sources

- ChatFin Dashboard UI Kit
- Personal finance Dashboard UI
- SaaS Dashboard / UI Kit
- Dashboard UI Kit / Free Admin Dashboard

This document is a distillation of their shared visual logic, not a copy of any single screen.
