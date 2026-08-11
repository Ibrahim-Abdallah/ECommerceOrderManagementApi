# E-Commerce Order Management API — Project Plan

## 1. Project Purpose

Build a portfolio-ready **ASP.NET Core REST API for e-commerce order management** that demonstrates practical backend engineering skills beyond basic CRUD.

The project should showcase:

- REST API design
- Authentication and authorization
- JWT access tokens
- Refresh-token rotation and revocation
- Role-based authorization
- Entity Framework Core
- SQL Server
- Real-world business rules
- Shopping cart workflow
- Order processing
- Inventory management
- Database transactions
- Concurrency-safe stock updates
- Pagination, filtering, searching, and sorting
- Validation
- Centralized error handling
- Dapper for reporting
- Automated tests
- OpenAPI / Scalar documentation
- Clean, maintainable code
- Professional GitHub portfolio presentation

The application domain will be an **E-Commerce Order Management API**.

The goal is not to build a complete Amazon-style platform. The project should remain focused, understandable, and realistic for freelance backend work.

---

# 2. Technology Stack

Use:

- .NET 10
- ASP.NET Core 10 Web API
- C#
- Entity Framework Core 10
- SQL Server
- JWT Bearer Authentication
- ASP.NET Core built-in OpenAPI
- Scalar interactive API documentation
- FluentValidation
- Dapper for reporting queries
- xUnit
- ASP.NET Core integration testing

Prefer framework-native functionality where practical.

Do not introduce libraries or architectural patterns unless they provide clear value.

---

# 3. Repository Structure

Repository name:

```text
ECommerceOrderManagementApi
```

Target structure:

```text
ECommerceOrderManagementApi/
├── src/
│   └── ECommerceOrderManagementApi/
├── tests/
│   └── ECommerceOrderManagementApi.Tests/
├── screenshots/
├── README.md
├── PROJECT_PLAN.md
├── .gitignore
├── global.json
└── ECommerceOrderManagementApi.slnx
```

Keep the solution simple.

Do not split the application into multiple class-library projects unless there is a concrete reason.

---

# 4. Application Areas

The application contains six main areas:

1. Authentication
2. Product Catalog
3. Shopping Cart
4. Order Processing
5. Administration
6. Reporting

---

# 5. Roles

Implement two roles:

```text
Customer
Admin
```

Newly registered accounts receive:

```text
Customer
```

Admin accounts are created only through safe development configuration or database administration.

---

# 6. Authentication

Implement:

```http
POST /api/auth/register
POST /api/auth/login
POST /api/auth/refresh-token
POST /api/auth/logout
```

Authentication behavior should follow the secure approach already proven in the Task Management API.

## Registration

Customer registration fields:

```text
FirstName
LastName
Email
Password
ConfirmPassword
```

Requirements:

- Normalize email.
- Email must be unique.
- Hash passwords using ASP.NET Core password hashing.
- Never persist plaintext passwords.
- Never expose password hashes.
- Assign the `Customer` role automatically.

---

# 7. JWT Authentication

JWT access tokens should contain only useful claims such as:

```text
UserId
Email
Role
Jti
```

Requirements:

- Validate issuer.
- Validate audience.
- Validate signing key.
- Validate token lifetime.
- Use UTC timestamps.
- Keep signing secrets outside source control.
- Use User Secrets or environment variables locally.

Suggested configuration:

```json
{
  "Jwt": {
    "Issuer": "ECommerceOrderManagementApi",
    "Audience": "ECommerceOrderManagementApi.Client",
    "Key": "",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  }
}
```

---

# 8. Refresh Tokens

Entity:

```text
RefreshToken
- Id
- TokenHash
- UserId
- CreatedAtUtc
- ExpiresAtUtc
- RevokedAtUtc
- ReplacedByTokenId
```

Requirements:

- Generate cryptographically secure random tokens.
- Persist hashes only.
- Support rotation.
- Support revocation.
- Reject expired tokens.
- Reject revoked tokens.
- Reject replay of previously rotated tokens.
- Logout revokes the submitted active refresh token.
- Never log raw access or refresh tokens.

---

# 9. Product Categories

Entity:

```text
Category
- Id
- Name
- Description
- IsActive
- CreatedAtUtc
- UpdatedAtUtc
```

Admin endpoints:

```http
POST   /api/categories
PUT    /api/categories/{id}
DELETE /api/categories/{id}
```

Public/customer endpoints:

```http
GET /api/categories
GET /api/categories/{id}
```

Rules:

- Category name is required.
- Category name must be unique after normalization where appropriate.
- Only active categories appear in normal catalog queries.
- A category containing active products cannot be deleted without an explicit safe rule.
- Prefer deactivation instead of destructive deletion where appropriate.

---

# 10. Products

Entity:

```text
Product
- Id
- Name
- Description
- Price
- StockQuantity
- CategoryId
- IsActive
- CreatedAtUtc
- UpdatedAtUtc
```

Admin endpoints:

```http
POST   /api/products
PUT    /api/products/{id}
DELETE /api/products/{id}
```

Catalog endpoints:

```http
GET /api/products
GET /api/products/{id}
```

## Product Rules

- Name is required.
- Price must be greater than zero.
- Stock quantity cannot be negative.
- Category must exist.
- Category must be active when assigning a product.
- Normal customers cannot create or modify products.
- Product deletion should behave as a safe deactivation rather than destroying historical order information.
- Inactive products cannot be added to carts.

---

# 11. Product Querying

`GET /api/products` supports:

```text
pageNumber
pageSize
search
categoryId
minPrice
maxPrice
sortBy
sortDirection
```

Example:

```http
GET /api/products?search=keyboard&categoryId=3&minPrice=20&maxPrice=200&pageNumber=1&pageSize=10&sortBy=price&sortDirection=asc
```

Search at minimum:

- Product name
- Product description

Supported sorting should use an explicit whitelist.

Suggested fields:

```text
name
price
createdAt
stock
```

Suggested defaults:

```text
pageNumber = 1
pageSize = 10
sortBy = createdAt
sortDirection = desc
```

Maximum page size:

```text
100
```

Do not support arbitrary dynamic property names.

---

# 12. Shopping Cart

Each Customer has one active cart.

Entities:

```text
Cart
- Id
- UserId
- CreatedAtUtc
- UpdatedAtUtc
```

```text
CartItem
- Id
- CartId
- ProductId
- Quantity
- CreatedAtUtc
- UpdatedAtUtc
```

Database constraints should prevent duplicate product rows in the same cart.

Recommended unique constraint:

```text
CartId + ProductId
```

---

# 13. Cart Endpoints

Customer-only endpoints:

```http
GET    /api/cart
POST   /api/cart/items
PUT    /api/cart/items/{productId}
DELETE /api/cart/items/{productId}
DELETE /api/cart
```

Example add request:

```json
{
  "productId": 10,
  "quantity": 2
}
```

Example quantity update:

```json
{
  "quantity": 4
}
```

---

# 14. Cart Business Rules

When adding or updating a cart item:

- Customer must be authenticated.
- Product must exist.
- Product must be active.
- Product category must be available where applicable.
- Quantity must be greater than zero.
- Requested quantity must not exceed currently available stock.
- Customer cannot access another customer's cart.

The cart response should calculate display totals using current product prices.

Important:

The cart is not a final sales record.

Product prices may change before checkout.

The authoritative price is captured only when the order is created.

---

# 15. Orders

Entities:

```text
Order
- Id
- UserId
- Status
- TotalAmount
- CreatedAtUtc
- UpdatedAtUtc
```

```text
OrderItem
- Id
- OrderId
- ProductId
- ProductName
- UnitPrice
- Quantity
- TotalPrice
```

Order items intentionally store:

```text
ProductName
UnitPrice
```

as snapshots.

This ensures historical orders remain correct even if a product is renamed or its price changes later.

---

# 16. Order Status

Use an enum:

```text
Pending
Confirmed
Shipped
Delivered
Cancelled
```

Normal forward transitions:

```text
Pending
   ↓
Confirmed
   ↓
Shipped
   ↓
Delivered
```

Customer cancellation:

```text
Pending → Cancelled
```

Terminal statuses:

```text
Delivered
Cancelled
```

Invalid status transitions must be rejected.

Examples:

```text
Delivered → Pending     ❌
Cancelled → Confirmed   ❌
Shipped → Pending       ❌
```

---

# 17. Checkout

Customer endpoint:

```http
POST /api/orders
```

The endpoint creates an order from the authenticated customer's active cart.

The client must NOT send:

```text
UserId
TotalAmount
UnitPrice
ProductName
```

These values are determined server-side.

---

# 18. Checkout Workflow

Checkout should perform:

```text
Load authenticated customer's cart
        ↓
Validate cart contains items
        ↓
Load required product data
        ↓
Validate products are active
        ↓
Revalidate stock
        ↓
Reserve/decrease stock safely
        ↓
Create Order
        ↓
Create OrderItems with price/name snapshots
        ↓
Calculate totals server-side
        ↓
Clear cart
        ↓
Commit transaction
```

All checkout database changes must execute inside a database transaction.

If any step fails:

```text
ROLLBACK
```

No partial order should remain.

No partial inventory deduction should remain.

---

# 19. Stock Concurrency

Checkout must protect against overselling.

Example:

```text
Stock = 1

Customer A checks out quantity 1
Customer B checks out quantity 1 at the same time
```

Only one checkout may succeed.

Do not rely only on:

```text
Read StockQuantity
if enough:
    StockQuantity -= quantity
```

because concurrent requests can both observe the same original stock.

Use a concurrency-safe database operation.

Preferred approach:

- Execute an atomic conditional stock decrement.
- Update only when:

```text
StockQuantity >= requestedQuantity
```

- Verify that exactly one row was affected.
- Treat zero affected rows as insufficient/concurrently consumed stock.

The stock change must be part of the checkout transaction.

This is a key portfolio feature.

---

# 20. Price Calculation

Never trust totals supplied by the client.

For each order item:

```text
TotalPrice = UnitPrice × Quantity
```

Order total:

```text
TotalAmount = SUM(OrderItem.TotalPrice)
```

Use decimal types suitable for currency.

Example:

```csharp
decimal
```

Do not use floating-point types such as `double` for money.

Configure appropriate SQL precision.

Suggested:

```text
decimal(18,2)
```

---

# 21. Customer Order Endpoints

Authenticated Customer:

```http
GET  /api/orders
GET  /api/orders/{id}
POST /api/orders
POST /api/orders/{id}/cancel
```

Customers may access only their own orders.

Do not accept a customer ID from query or route parameters to determine ownership.

Derive ownership from JWT claims.

Cross-user order access should return an access-safe response such as:

```text
404 Not Found
```

---

# 22. Customer Order Listing

`GET /api/orders` should support pagination.

Suggested query parameters:

```text
pageNumber
pageSize
status
fromDate
toDate
```

Sorting can remain simple and deterministic:

```text
CreatedAtUtc DESC
Id DESC
```

Avoid adding unnecessary generic sorting unless useful.

---

# 23. Customer Cancellation

Endpoint:

```http
POST /api/orders/{id}/cancel
```

Rules:

- Order must belong to authenticated customer.
- Order must exist.
- Only `Pending` orders may be cancelled by the customer.
- Set status to `Cancelled`.
- Restore inventory quantities.
- Stock restoration and status change must occur in one transaction.
- Repeated invalid cancellation attempts must not duplicate inventory restoration.

---

# 24. Administration — Orders

Admin endpoints:

```http
GET   /api/admin/orders
GET   /api/admin/orders/{id}
PATCH /api/admin/orders/{id}/status
```

Admin list filters may include:

```text
pageNumber
pageSize
status
customerEmail
fromDate
toDate
```

Admin responses must not expose:

- Password hashes
- Refresh-token hashes
- JWT secrets
- Internal security configuration

---

# 25. Admin Order Status Rules

Admin may perform valid forward transitions:

```text
Pending → Confirmed
Confirmed → Shipped
Shipped → Delivered
```

Reject invalid transitions.

The API should centralize transition rules instead of scattering status checks across controllers.

Controllers must remain thin.

---

# 26. Reporting

Add a small Admin-only reporting area using **Dapper**.

Do not rebuild the entire application persistence layer using Dapper.

The purpose is to demonstrate practical use of both:

```text
EF Core
+
Dapper
```

Recommended endpoint:

```http
GET /api/admin/reports/sales-summary
```

Suggested filters:

```text
fromDate
toDate
```

Suggested response:

```json
{
  "totalOrders": 120,
  "totalRevenue": 85000.00,
  "averageOrderValue": 708.33,
  "topSellingProducts": [
    {
      "productId": 10,
      "productName": "Example Product",
      "quantitySold": 45,
      "revenue": 12500.00
    }
  ]
}
```

Only successful/non-cancelled orders should contribute according to a documented reporting rule.

Keep reporting intentionally small.

---

# 27. Entity Relationships

Core relationships:

```text
User
 ├── RefreshTokens
 ├── Cart
 │    └── CartItems
 │          └── Product
 │
 └── Orders
      └── OrderItems
            └── Product

Category
 └── Products
```

Suggested relationships:

```text
User 1 ---- * RefreshToken
User 1 ---- 1 Cart
Cart 1 ---- * CartItem
Product 1 ---- * CartItem

Category 1 ---- * Product

User 1 ---- * Order
Order 1 ---- * OrderItem
Product 1 ---- * OrderItem
```

---

# 28. Database Constraints

Add appropriate:

- Primary keys
- Foreign keys
- Unique indexes
- Required fields
- Length limits
- Decimal precision
- Search/query indexes where useful

Important unique constraints:

```text
User.NormalizedEmail
Category.Name / normalized equivalent
Cart.UserId
CartItem(CartId, ProductId)
```

Use deliberate delete behavior.

Do not allow cascading deletes to accidentally destroy historical orders.

---

# 29. Architecture

Recommended application structure:

```text
src/ECommerceOrderManagementApi/
├── Common/
│   ├── Errors/
│   └── Pagination/
├── Configuration/
├── Controllers/
│   ├── AuthController.cs
│   ├── CategoriesController.cs
│   ├── ProductsController.cs
│   ├── CartController.cs
│   ├── OrdersController.cs
│   └── Admin/
│       ├── AdminOrdersController.cs
│       └── ReportsController.cs
├── Data/
│   ├── AppDbContext.cs
│   ├── Configurations/
│   └── Migrations/
├── DTOs/
│   ├── Auth/
│   ├── Categories/
│   ├── Products/
│   ├── Cart/
│   ├── Orders/
│   ├── Admin/
│   └── Reports/
├── Entities/
├── Enums/
├── Interfaces/
├── Services/
├── Validation/
└── Program.cs
```

---

# 30. Architecture Rules

- Controllers remain thin.
- Services contain use-case and business logic.
- EF Core `AppDbContext` may be used directly by services.
- Do not add a generic repository wrapper around EF Core.
- Dapper is limited primarily to reporting.
- Do not use CQRS/MediatR unless a concrete need appears.
- Avoid unnecessary inheritance.
- Avoid premature abstractions.
- Prefer readable code over architecture ceremony.

---

# 31. DTO Rules

Never expose EF Core entities directly.

Use request and response DTOs.

Examples:

```text
RegisterRequest
LoginRequest
RefreshTokenRequest
AuthResponse

CreateCategoryRequest
UpdateCategoryRequest
CategoryResponse

CreateProductRequest
UpdateProductRequest
ProductResponse
ProductQueryParameters

AddCartItemRequest
UpdateCartItemRequest
CartResponse
CartItemResponse

OrderResponse
OrderDetailsResponse
OrderItemResponse
OrderQueryParameters

UpdateOrderStatusRequest

SalesSummaryResponse
TopSellingProductResponse
```

---

# 32. Validation

Use FluentValidation.

Validate at minimum:

## Authentication

- Required first name
- Required last name
- Valid email
- Password strength
- Password confirmation

## Category

- Required name
- Length constraints

## Product

- Required name
- Positive price
- Non-negative stock
- Valid category
- Length constraints

## Cart

- Valid product ID
- Quantity greater than zero

## Query Parameters

- Page number >= 1
- Page size between 1 and 100
- Valid sort field
- Valid sort direction
- Valid price range
- Valid date range

## Order Status

- Valid enum value
- Valid transition enforced by business rules

---

# 33. Error Handling

Use centralized ASP.NET Core Problem Details.

Expected responses include:

```text
200 OK
201 Created
204 No Content
400 Bad Request
401 Unauthorized
403 Forbidden
404 Not Found
409 Conflict
500 Internal Server Error
```

Examples:

```text
Invalid validation            → 400
Invalid credentials           → 401
Normal user calling Admin API → 403
Missing/foreign order         → 404
Duplicate email               → 409
Concurrent stock unavailable  → 409
Unexpected exception          → 500
```

Production `500` responses must not expose:

- Stack traces
- Exception types
- SQL details
- Connection strings
- Secrets
- Internal file paths

Include a request/trace identifier where useful.

---

# 34. Logging

Use:

```text
ILogger<T>
```

Useful events may include:

- Successful order creation
- Order cancellation
- Admin status transition
- Unexpected exception
- Stock conflict

Never log:

- Passwords
- Password hashes
- JWT signing keys
- Raw access tokens
- Raw refresh tokens
- Refresh-token hashes
- Authorization headers

Avoid excessive logging of personal information.

---

# 35. OpenAPI / Scalar

Use ASP.NET Core built-in OpenAPI document generation with Scalar.

Requirements:

- Bearer authentication scheme
- Protected endpoints show security requirements
- Useful endpoint summaries
- Request/response documentation
- Representative error responses
- Development-only interactive documentation

Expected demo flow:

```text
Register
   ↓
Login
   ↓
Authorize
   ↓
Browse Products
   ↓
Add to Cart
   ↓
Checkout
   ↓
View Order
```

Admin demo:

```text
Admin Login
   ↓
Create Product
   ↓
View Orders
   ↓
Update Order Status
   ↓
View Sales Report
```

---

# 36. Automated Testing

Use meaningful unit and integration tests.

Do not chase an arbitrary coverage percentage.

Test important behavior.

## Authentication

- Register succeeds
- Duplicate email rejected
- Login succeeds
- Invalid credentials rejected
- Refresh succeeds
- Rotated refresh token cannot be reused
- Revoked token rejected
- Logout works

## Authorization

- Anonymous customer endpoints rejected
- Customer cannot call Admin endpoints
- Admin endpoints work for Admin

## Products

- Admin can create product
- Customer cannot create product
- Invalid price rejected
- Invalid category rejected
- Inactive product excluded where expected
- Pagination/filter/search/sort work

## Cart

- Add valid item
- Invalid quantity rejected
- Inactive product rejected
- Quantity exceeding stock rejected
- Update quantity
- Remove item
- Customer isolation enforced

## Checkout

- Empty cart rejected
- Valid checkout creates order
- Order item snapshots product name
- Order item snapshots price
- Server calculates totals
- Stock decreases correctly
- Cart clears after success
- Failure rolls back all changes
- Concurrent stock conflict does not oversell

## Orders

- Customer sees own orders
- Customer cannot read another customer's order
- Customer can cancel Pending order
- Customer cannot cancel non-Pending order
- Cancellation restores stock once

## Administration

- Admin can list orders
- Admin can read order details
- Valid status transition succeeds
- Invalid status transition rejected

## Reporting

- Sales totals calculated correctly
- Cancelled orders excluded according to rule
- Top products calculated correctly

## Error Handling

- Validation returns Problem Details
- Unauthorized response correct
- Forbidden response correct
- Not-found ownership hiding works
- Unexpected exception does not leak internals

---

# 37. Security Checklist

Before declaring the project complete:

- [ ] Passwords are hashed
- [ ] JWT signing secret is not committed
- [ ] JWT issuer validated
- [ ] JWT audience validated
- [ ] JWT lifetime validated
- [ ] JWT signature validated
- [ ] Refresh tokens are cryptographically random
- [ ] Refresh tokens are stored hashed
- [ ] Rotation implemented
- [ ] Revoked token replay rejected
- [ ] Customer identity comes from JWT claims
- [ ] Cart ownership enforced
- [ ] Order ownership enforced
- [ ] Customers cannot assign their own prices
- [ ] Customers cannot assign totals
- [ ] Customers cannot assign another UserId
- [ ] Product modification requires Admin
- [ ] Order administration requires Admin
- [ ] Stock checkout protects against overselling
- [ ] Checkout uses a database transaction
- [ ] Cancellation restores inventory atomically
- [ ] Sensitive values are not logged
- [ ] Production errors do not expose internals
- [ ] HTTPS redirection enabled

---

# 38. Scope Boundaries

Version 1 intentionally excludes:

```text
Frontend UI
Payment gateways
Stripe
PayPal
Shipping integrations
Email notifications
SMS
Product reviews
Wishlists
Discount coupons
Promotions
Multiple currencies
Tax engines
Multiple warehouses
Returns/refunds workflow
Microservices
RabbitMQ
Kafka
Redis
Event sourcing
CQRS
MediatR
Docker orchestration
Kubernetes
Elasticsearch
Recommendation engines
Real-time notifications
```

Do not add these unless explicitly requested later.

---

# 39. Definition of Done

The project is complete only when:

- [ ] Solution builds successfully
- [ ] Database migrations work
- [ ] Registration works
- [ ] Login works
- [ ] JWT authentication works
- [ ] Refresh-token rotation works
- [ ] Logout works
- [ ] Customer role works
- [ ] Admin role works
- [ ] Category management works
- [ ] Product management works
- [ ] Product search/filtering/pagination works
- [ ] Shopping cart works
- [ ] Checkout works
- [ ] Order snapshots preserve historical prices/names
- [ ] Stock updates are concurrency-safe
- [ ] Checkout transaction rollback works
- [ ] Customer order ownership is enforced
- [ ] Customer cancellation works
- [ ] Cancellation restores stock
- [ ] Admin order management works
- [ ] Status transition rules work
- [ ] Dapper sales reporting works
- [ ] FluentValidation works
- [ ] Problem Details works
- [ ] OpenAPI/Scalar authentication works
- [ ] Automated tests pass
- [ ] Secrets are not committed
- [ ] README is complete
- [ ] Genuine API screenshots are included
- [ ] Repository is ready for Freelancer/GitHub portfolio use

Before final completion run:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-restore
```

All commands must succeed.

---

# 40. Implementation Phases

Implementation must proceed one phase at a time.

Each phase gets:

```text
Dedicated Git branch
Focused implementation
Automated tests
Build verification
Pull request
Merge into master
```

---

## Phase 1 — Foundation

Branch:

```text
phase/01-foundation
```

Implement:

- Solution/project structure
- Test project
- EF Core
- SQL Server
- Core entities
- Entity configurations
- Relationships
- Database constraints
- Initial migration
- OpenAPI
- Scalar
- Configuration models
- Base test infrastructure

Core entities created in this phase:

```text
User
RefreshToken
Category
Product
Cart
CartItem
Order
OrderItem
```

Verify:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-restore
```

---

## Phase 2 — Authentication

Branch:

```text
phase/02-authentication
```

Implement:

- Registration
- Email normalization
- Password hashing
- Login
- JWT generation
- JWT validation
- Customer role
- Current user service
- Bearer OpenAPI support
- Authentication tests

---

## Phase 3 — Refresh Tokens

Branch:

```text
phase/03-refresh-tokens
```

Implement:

- Secure token generation
- SHA-256 token persistence
- Refresh endpoint
- Rotation
- Revocation
- Replay rejection
- Logout
- Tests

---

## Phase 4 — Product Catalog

Branch:

```text
phase/04-product-catalog
```

Implement:

- Category CRUD
- Product CRUD
- Admin authorization
- Safe product deactivation
- Product query parameters
- Pagination
- Filtering
- Search
- Sorting
- Validation
- Tests

---

## Phase 5 — Shopping Cart

Branch:

```text
phase/05-shopping-cart
```

Implement:

- Customer cart
- Add item
- Update quantity
- Remove item
- Clear cart
- Product/stock validation
- Cart ownership
- Calculated display totals
- Tests

---

## Phase 6 — Order Processing

Branch:

```text
phase/06-order-processing
```

This is the project's most important phase.

Implement:

- Checkout endpoint
- Order creation
- Order item snapshots
- Server-side totals
- Database transaction
- Concurrency-safe stock decrement
- Overselling protection
- Clear cart after success
- Rollback on failure
- Checkout integration tests
- Stock concurrency tests

---

## Phase 7 — Order Management

Branch:

```text
phase/07-order-management
```

Implement:

- Customer order list
- Customer order details
- Ownership enforcement
- Customer cancellation
- Inventory restoration
- Admin order listing
- Admin order details
- Status transitions
- Invalid transition protection
- Tests

---

## Phase 8 — Reporting

Branch:

```text
phase/08-reporting
```

Implement:

- Dapper connection access
- Admin sales summary endpoint
- Revenue calculation
- Order counts
- Average order value
- Top-selling products
- Date filtering
- Reporting tests

Keep Dapper usage focused.

Do not rewrite normal application services using Dapper.

---

## Phase 9 — API Quality & Portfolio Polish

Branch:

```text
phase/09-portfolio-polish
```

Implement/refine:

- FluentValidation consistency
- Problem Details
- Centralized exception handling
- Logging audit
- CancellationToken propagation
- OpenAPI summaries/descriptions
- Security review
- README
- Setup instructions
- Authentication demo
- Checkout demo
- Endpoint documentation
- Architecture diagram
- Testing instructions
- Freelancer positioning
- Genuine Scalar/API screenshots

Final verification:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-restore
git diff --check
```

---

# 41. Git Workflow

Never implement the entire project directly on `master`.

Workflow:

```text
master
  ↓
phase/01-foundation
  ↓
Pull Request
  ↓
master
  ↓
phase/02-authentication
  ↓
Pull Request
  ↓
...
```

Each PR should contain:

- Clear summary
- Included functionality
- Security considerations
- Tests added
- Validation commands actually run
- Build/test results
- Database migration notes
- Explicit scope boundary

Do not claim a command passed unless it was actually executed.

---

# 42. Code Quality Rules

Follow these rules throughout the project:

- Nullable reference types enabled
- Async database APIs
- CancellationToken where useful
- Dependency injection
- Thin controllers
- Business logic in services
- No generic repository abstraction
- No unnecessary inheritance
- No static service locator
- No secrets in source control
- No sensitive values in logs
- No arbitrary dynamic SQL
- Parameterized Dapper queries
- Explicit sorting whitelists
- Decimal for monetary values
- UTC timestamps
- Clear naming
- Focused methods
- Comments explain why, not obvious what
- Build with zero errors
- Resolve meaningful warnings
- Tests must protect important business rules

---

# 43. Portfolio Positioning

Suggested portfolio title:

> ASP.NET Core E-Commerce Order Management API with Secure Checkout & Inventory Control

Suggested description:

> Production-oriented REST API built with ASP.NET Core, EF Core, SQL Server, JWT authentication, transactional order processing, inventory control, Dapper reporting, validation, automated tests, and OpenAPI documentation.

Skills demonstrated:

```text
ASP.NET Core
.NET
C#
REST API
JWT
Authentication
Authorization
Entity Framework Core
SQL Server
Dapper
Database Transactions
Inventory Management
Business Logic
FluentValidation
OpenAPI
Automated Testing
Backend Development
```

The main portfolio differentiator is:

```text
Secure transactional checkout
+
Historical order snapshots
+
Concurrency-safe stock management
```

This makes the project materially different from the Task Management API and demonstrates backend business logic suitable for real freelance client work.