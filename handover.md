# Ghars Platform Handover

## 1. Document Purpose

This document is a full business and technical handover for the `Ghars Platform` project. It is intended for:

- new developers joining the project
- technical leads and solution architects
- business analysts and product owners
- QA engineers
- future AI coding agents and ChatGPT sessions

The goal is to make the system understandable without relying on prior chat history or tribal knowledge.

This handover combines:

- the business understanding from the Ghars SRS
- the current live codebase structure
- the implementation baseline in the current ASP.NET Core MVC project
- risks, workflows, modules, and development guidance

## 2. Source of Truth and Working Assumptions

### Primary source of truth
The main source of truth for business requirements is the Ghars SRS provided by the user.

### Secondary source of truth
The current codebase is the implementation baseline. It may include:

- features already implemented from the SRS
- evolved modules beyond the original SRS summary
- partial implementations of future-state or extended requirements

### Rule for future work
If the SRS and the code differ:

- treat the SRS as the intended business truth
- treat the code as the current operational reality
- reconcile both before making non-trivial changes

### Supporting project documents
The user also provided separate Ghars documents for:

- general program understanding
- booking system
- reports and statistics
- digital library
- KPIs
- gallery
- surveys
- reports

These should be treated as supporting specification context beneath the main SRS.

## 3. Project Overview

### Project name
`Ghars Platform`

### Business purpose
Ghars Platform is a bilingual Dubai Sports Council platform focused on values-driven sports culture, youth development, club and partner collaboration, program execution, participation tracking, and measurable outcomes.

### Strategic objectives
The system exists to:

- coordinate Ghars programs, lectures, workshops, trainings, courses, and events
- govern participation through controlled organization and booking workflows
- track attendance and issue verifiable certificates
- capture feedback and satisfaction through surveys
- distribute learning content through a digital library
- incentivize engagement through points and rewards
- support DSC oversight with dashboards, reports, notifications, and KPIs

### Why the system matters
This is not just a marketing or informational site. It is a governance and operations platform. It controls:

- who is approved to participate
- what activities are published
- how bookings move between clubs and implementing partners
- how attendance is captured
- how outcomes are reported and audited

Any change can affect:

- workflows
- dashboards
- reporting
- role access
- audit trails
- user trust

## 4. High-Level Product Model

At a high level, the platform works like this:

1. Users and organizations are created and approved.
2. DSC or implementing entities manage and publish learning programs.
3. Clubs discover available programs and submit booking requests.
4. Partners review, approve, reject, or propose alternate times.
5. Confirmed activities drive attendance and downstream agenda/KPI behavior.
6. Users complete surveys, access library content, earn points, and redeem rewards.
7. DSC and organization roles monitor everything via dashboards, reports, and notifications.

## 5. Current Technical Architecture

### Framework and stack
- `ASP.NET Core MVC (.NET 8)`
- `Entity Framework Core`
- `SQL Server`
- `ASP.NET Core Identity`
- `SignalR`
- `Bootstrap 5`
- `Chart.js`
- helper-based QR and certificate PDF generation

### Architectural style
The application uses a compact MVC structure:

- controllers contain much of the workflow logic
- `AppDbContext` is used directly in controllers
- there is no repository pattern
- there is no generalized service layer
- role-based authorization is enforced with attributes/policies

### Why this matters
Future contributors must understand that business rules are often embedded in:

- controllers
- direct EF queries
- entity status fields
- helper methods
- Razor views

This means changes must be assessed across controller logic, view rendering, and reporting assumptions.

## 6. Project Structure

### Root folders and responsibilities

- `Controllers/`
  - public/member flows and cross-cutting controllers
- `Controllers/Admin/`
  - admin and DSC operational modules
- `Models/Core/`
  - main domain entities and enums
- `Models/Identity/`
  - user and role constants/extensions
- `ViewModels/`
  - form/view models used in public flows
- `Data/`
  - `AppDbContext`, model config, and seeding
- `Hubs/`
  - SignalR notification hub
- `Helpers/`
  - QR/certificate-related helpers
- `Views/`
  - public and member-facing Razor UI
- `Areas/Admin/Views/`
  - admin-specific Razor views
- `wwwroot/`
  - CSS, JS, uploads, theme assets, logos, and public static content
- `Migrations/`
  - EF Core schema migrations

## 7. Authentication, Authorization, and Localization

### Authentication
The platform uses `ASP.NET Core Identity` with:

- unique email requirement
- strong password rules
- lockout behavior
- cookie authentication

### Main roles
- `Super Admin`
- `DSC Admin`
- `Club Admin`
- `Academy Admin`
- `Partner Admin`
- `Speaker`
- `Viewer`

### Authorization pattern
- admin area mostly restricted to `Super Admin` and `DSC Admin`
- club and partner flows are protected by role-based `[Authorize]` attributes
- some workflows also depend on organization membership, not just role

### Localization
The system supports:

- `en`
- `ar`

Localization behavior:

- cookie-based and query-string culture switching
- Arabic mode must render in RTL
- English mode renders LTR
- many views already use bilingual label helpers

## 8. Core Domain Model

### Identity and access
- `ApplicationUser`
- `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`

### Program and calendar
- `Season`
- `CalendarEvent`
- `Activity`
- `ActivitySpeaker`

### Organization and participants
- `Organization`
- `OrganizationContact`
- `OrganizationDocument`
- `OrganizationAdminLink`
- `PartnerProfile`
- `SpeakerProfile`

### Booking and workflow
- `BookingRequest`
- `BookingAuditTrail`
- `BookingProposedTimeOption`

### Attendance and certificates
- `AttendanceSession`
- `AttendanceRecord`
- `Certificate`
- `CertificateTemplate`

### Feedback
- `Survey`
- `SurveyQuestion`
- `SurveyOption`
- `SurveyResponse`
- `SurveyAnswer`

### Content and media
- `NewsItem`
- `SuccessStory`
- `MediaAlbum`
- `MediaItem`
- `LibraryCategory`
- `LibraryItem`
- `GalleryItem`

### Incentives
- `UserPointsWallet`
- `PointsTransaction`
- `Reward`
- `RewardRedemption`

### Notifications and audit
- `Notification`
- `NotificationDelivery`
- `SystemAuditLog`

### Evolved operational modules
- `AgendaEntry`
- `AgendaMedia`
- `KpiSubmission`
- `KpiDocument`

## 9. Business Modules

## 9.1 Authentication and User Management

### Purpose
Provide login, logout, seeded users, and secure role-based access.

### Business behavior
- platform access is controlled and not self-service for all admin roles
- organization-linked users drive downstream workflows
- role assignment controls which dashboard and workflow a user can access

### Important implementation notes
- configured in `Program.cs`
- handled by `AccountController`
- seeded using `DbSeeder`

## 9.2 Dashboard Module

### Purpose
Provide operational visibility for admins, clubs, and partners.

### Current dashboard variants
- admin dashboard
- club dashboard
- partner dashboard

### Typical metrics
- organizations
- activities/programs
- bookings
- attendance
- certificates
- surveys
- points/rewards
- pending actions
- program trends

### Business importance
Dashboard outputs are management-facing. Any workflow change that affects counts or statuses also affects decision-making.

## 9.3 Organizations Module

### Purpose
Register and govern clubs, academies, partners, and government entities.

### Business behavior
- registration creates organization-related records
- approval status determines whether the organization can participate downstream
- organization-user links define operational access

### Risks
- access leakage if organization scoping is wrong
- workflow breakage if approval status is not respected

## 9.4 Activities / Learning Programs Module

### Purpose
Represent Ghars programs such as lectures, workshops, training sessions, courses, and events.

### Core fields
- season
- type
- title EN/AR
- description EN/AR
- timing
- location
- capacity
- publication status
- partner organization linkage

### Business behavior
- only published activities should be discoverable/bookable
- these activities are the anchor for booking, attendance, and reporting

## 9.5 Booking Module

### Purpose
Allow clubs to request booking of partner/implementing-entity programs and let partner-side users review them.

### Current evolved implementation
The project includes an evolved booking workflow beyond the thin baseline from the SRS:

- club-side booking request page
- partner-side review
- alternate time proposal
- club response to proposed times
- dashboard visibility
- notifications
- audit trail

### Current booking details captured
- linked activity/program
- club organization
- partner organization
- proposed start/end datetime
- target audience selections
- optional other target audience
- expected participants
- audience detail notes
- contact person details
- logistics / special requirements
- club notes
- audit history

### Important status lifecycle
- `Pending` / `PendingPartnerApproval`
- `Approved`
- `Rejected`
- `Cancelled`
- `PartnerProposedNewTime`
- `Confirmed`
- `ClubRejectedProposedTimes`

### Business importance
This is one of the most sensitive modules because it connects:

- clubs
- partners
- activity schedules
- notifications
- dashboards
- future agenda/KPI/reporting logic

## 9.6 Attendance Module

### Purpose
Record actual participation through QR-backed attendance sessions.

### Workflow
- admin creates session
- system generates QR token
- user scans token and checks in
- record is saved

### Business importance
Attendance is a proof-of-participation event and may feed:

- certificates
- points
- reports
- KPI logic

## 9.7 Certificates Module

### Purpose
Issue and verify proof of participation.

### Key behaviors
- generate certificate record
- create QR/token
- save PDF
- public verification
- revocation support

### Business importance
Certificate verification is public trust-sensitive and should not be broken by refactors.

## 9.8 Surveys Module

### Purpose
Capture participant feedback and satisfaction.

### Question types
- stars
- yes/no
- text
- MCQ

### Rules
- one response per survey per user
- aggregation supports reporting
- may award points

## 9.9 Digital Library Module

### Purpose
Distribute educational content and track access.

### Features
- categories
- uploaded assets
- public/protected visibility
- optional points cost
- content metrics

### Known business preference
The SRS emphasizes preview-first behavior over direct-download-first behavior where content protection matters.

## 9.10 Rewards and Points Module

### Purpose
Reward engagement and allow point-based redemption.

### Core model
- wallet
- transaction ledger
- reward catalog
- redemption status lifecycle

### Business importance
Wallet and transaction consistency must remain reliable.

## 9.11 News, Success Stories, and Public Content

### Purpose
Support communication, storytelling, and public-facing Ghars identity.

### Features
- news ticker
- success stories
- partner display
- public informational pages

## 9.12 Notifications Module

### Purpose
Handle operational communication and action alerts.

### Features
- persisted notifications
- per-user delivery rows
- unread/read tracking
- SignalR live notifications

### Business importance
Notifications are part of workflow execution, not just UI decoration.

## 9.13 Reports and Analytics

### Purpose
Provide operational insight and management reporting.

### Report subjects
- bookings
- attendance
- certificates
- surveys and satisfaction
- points/rewards
- dashboard analytics

### Business importance
Reports are downstream consumers of data truth. Broken status semantics or missing relationships will create misleading reports.

## 9.14 Agenda, KPI, and Gallery Modules

### Purpose
These modules extend the original baseline and reflect project evolution.

### Why they matter
They show that the codebase is broader than the original simple summary. Future changes to booking, attendance, media, or seasonal reporting should inspect these modules before implementation.

## 10. User Roles and What They Can Do

## 10.1 Super Admin
- full authority
- can access admin governance flows
- should be treated as unrestricted operational authority

## 10.2 DSC Admin
- operational administrative role
- manages core system modules, approvals, dashboards, and reports

## 10.3 Club Admin
- can view available programs
- can submit booking requests
- can monitor bookings from the club dashboard
- can respond to partner-proposed times
- should only access club-scoped data

## 10.4 Academy Admin
- organization-specific access for academy-related use cases

## 10.5 Partner Admin
- can review club booking requests
- can approve, reject, or propose alternate time options
- should only access partner-relevant activities and bookings

## 10.6 Speaker
- intended speaker-related access

## 10.7 Viewer
- authenticated participant role
- can use member-facing functions like surveys, rewards, certificates, notifications, attendance, and library

## 11. Major Workflows

## 11.1 Organization Registration and Approval
1. Applicant submits registration.
2. System stores organization/contact/document data.
3. Admin reviews submission.
4. Status becomes approved or rejected.
5. Approved organizations become eligible for participation.

## 11.2 Activity Publication
1. Admin creates activity.
2. Activity starts as draft or controlled status.
3. Activity is published.
4. Public and organization-facing discovery becomes available.

## 11.3 Booking Submission
1. Club user clicks `Book`.
2. Booking request form opens.
3. Program and organization context is preloaded.
4. Club enters proposed schedule and audience/contact/logistics information.
5. Request is validated.
6. Booking is saved with pending partner approval status.
7. Notification is sent to the partner side.
8. Club sees a reference number and current status.

## 11.4 Partner Booking Review
1. Partner opens booking details.
2. Partner reviews club request data.
3. Partner chooses to:
   - approve
   - reject
   - propose new times
4. Club is notified.

## 11.5 Club Response to Proposed Times
1. Club sees a partner-proposed-time state.
2. Club accepts one proposed option or rejects them all.
3. Confirmed bookings move forward in the operational chain.

## 11.6 Attendance Workflow
1. Attendance session is created.
2. QR token is generated.
3. User scans token.
4. Attendance record is stored.

## 11.7 Certificate Workflow
1. Certificate is issued.
2. QR/token and PDF are generated.
3. Public verification is supported.
4. Revocation is possible.

## 11.8 Survey Workflow
1. Survey is created.
2. User completes the survey once.
3. Answers are stored.
4. Results feed satisfaction analytics.

## 11.9 Notification Workflow
1. System or admin creates notification.
2. Notification deliveries are resolved to users.
3. SignalR push occurs.
4. User sees status in inbox/topbar/dashboard.

## 11.10 Reporting Workflow
1. Authorized user opens dashboard/report.
2. Filters are applied.
3. Data is aggregated via EF queries.
4. Table/chart/KPI output is rendered.

## 12. Validation and Business Rules

### Cross-cutting rules
- required fields must be enforced
- role authorization must be preserved
- organization scoping must be preserved
- bilingual content should not degrade into mixed-language UX
- workflow statuses must remain consistent across views, details pages, and dashboards

### Booking-specific rules
- a booking should not finalize just because `Book` was clicked
- proposed end time must be after proposed start time
- expected participants must be greater than zero
- other audience text is required only when `Others` is selected
- partner review must preserve club-supplied information

### Attendance rules
- duplicate check-in must be blocked
- token must be valid
- user must be authenticated

### Survey rules
- one response per user per survey
- answer data integrity must remain valid

### Certificate rules
- unique token and certificate number
- verification flow must remain reliable

### Points/rewards rules
- spending must respect wallet balance
- wallet and transaction history must stay aligned

## 13. Database and EF Core Notes

### Database engine
`SQL Server`

### EF style
- Code First
- entity classes plus `AppDbContext`
- migrations stored under `Migrations/`

### Important EF notes from the project/SRS
- some entities use soft-delete query filters
- certain relationships use `DeleteBehavior.NoAction`
- survey cascade path handling was called out as a known DB concern

### Operational rule
If you change entities:

- update model classes
- update or add migrations
- inspect dashboards/reports/views that depend on those fields

## 14. Current Live Controllers and Responsibilities

### Public/member-facing
- `AccountController`
- `CultureController`
- `HomeController`
- `OrganizationController`
- `BookingsController`
- `AttendanceController`
- `NotificationsController`
- `LibraryController`
- `RewardsController`
- `SurveysController`
- `VerifyController`
- `ClubDashboardController`
- `PartnerDashboardController`
- `AgendaController`
- `KpiController`
- `GalleryController`

### Admin-facing
- `DashboardController`
- `OrganizationsController`
- `ActivitiesController`
- `BookingsController`
- `AttendanceController`
- `CertificatesController`
- `SurveysController`
- `LibraryController`
- `RewardsController`
- `NewsController`
- `NotificationsController`
- `ReportsController`
- `GalleryController`
- `KpiController`
- `UsersController`

## 15. UI/UX Expectations

### Brand and tone
- official, clean, government-grade layout
- Ghars and DSC identity should be preserved

### Language and direction
- Arabic and English are first-class
- Arabic must use RTL
- do not leave mixed English labels in Arabic mode

### Forms
- should be clear, sectioned, and professional
- validation messages should be understandable
- mobile responsiveness matters

### Dashboards
- clear cards
- filter panels
- readable charts and status indicators
- pending action visibility

### Accessibility
- semantic labels and headings
- usable forms
- sufficient contrast and clear focus/validation states

## 16. Security and Operational Concerns

### Security
- preserve authorization
- preserve org scoping
- avoid exposing protected files improperly
- do not hardcode production secrets

### Upload and file concerns
- library and uploads deserve careful validation and access control
- protected files under `wwwroot` are a business/security concern

### Notifications
- wrong targeting can leak workflow data to the wrong org

### Auditability
- critical actions should have auditable traces
- bookings already use `BookingAuditTrail`
- broader audit consistency remains an ongoing concern

## 17. Known Risks and Weak Areas

- localization may still be incomplete in some evolved modules
- workflow logic is distributed across controllers
- report/dashboard accuracy can drift if status semantics change
- audit writing may not be uniform across all modules
- org-based access mistakes can expose the wrong data
- file protection strategy may be weaker than desired for some content
- business logic concentration in controllers reduces maintainability
- evolved modules may not be fully reflected in the original SRS wording

## 18. Development Guidance for Future Contributors

### Before changing anything
1. Read the relevant SRS section.
2. Read the current controller and related views.
3. Identify impacted roles, statuses, and reports.

### Do not do these things casually
- do not remove existing features unless explicitly asked
- do not guess missing business rules
- do not change status names or workflow meanings without impact analysis
- do not break localization
- do not bypass role-based or org-based access

### When database fields change
- add or update migrations
- verify views and dashboards
- verify reports
- verify form validation

### When workflow logic changes
- verify club flow
- verify partner/admin flow
- verify notifications
- verify detail pages and dashboards

## 19. AI-Agent Working Instructions

Any AI agent working on this project should follow this sequence:

1. Understand the business request.
2. Check the relevant SRS/module context.
3. Inspect the actual code path in the current project.
4. Identify all impacted files:
   - controller
   - view
   - view model
   - entity
   - migration
   - dashboard/report
5. Implement the minimum safe change.
6. Build the project.
7. Verify workflow behavior and localization.
8. Report assumptions, limitations, and follow-up risks clearly.

## 20. Technical Run and Deployment Notes

### Basic local run requirements
- SQL Server available
- valid `DefaultConnection`
- migrations applied
- seed users/roles available

### Relevant files
- `Program.cs`
- `Data/AppDbContext.cs`
- `Data/DbSeeder.cs`
- `appsettings.json`
- `Properties/launchSettings.json`

### Startup behavior
- app configures MVC, localization, Identity, SignalR, EF Core
- app runs database seeding at startup

## 21. What Another Developer or AI Should Check First

If onboarding fresh, inspect in this order:

1. `handover.md`
2. project SRS and supporting docs
3. `Program.cs`
4. `Data/AppDbContext.cs`
5. `Models/Core/Enums.cs`
6. `Controllers/Public/BookingsController.cs`
7. `Controllers/Public/PartnerDashboardController.cs`
8. `Controllers/Admin/DashboardController.cs`
9. `Views/Bookings/`
10. `Views/ClubDashboard/` and `Views/PartnerDashboard/`

That path gives the fastest understanding of:

- auth
- localization
- entity model
- most critical workflow
- dashboards and role separation

## 22. Final Handover Summary

Ghars Platform is a business-critical bilingual operational platform for Dubai Sports Council. Its core strengths and sensitivities are:

- strong workflow dependence
- role and organization scoping
- direct relationship between business data and dashboards/reports
- cross-module impact of booking, attendance, and engagement changes

The safest way to work on this system is to:

- understand the business context first
- change only what is necessary
- verify role access and workflow integrity
- preserve localization and reporting integrity

This project should always be approached as a governed operations platform, not just a standard CRUD website.

