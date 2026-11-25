# External Integration Reference — Zarinpal Payment Gateway (API v4)

- **Provider Name:** Zarinpal (زرین‌پال)
- **Official Documentation Portal:** https://www.zarinpal.com/docs/paymentGateway/
- **API Version:** Payment Gateway REST API v4 (JSON)
- **Date Verified:** 2026-11-20
- **Implementation Status:** Contract verified; integration service pending in Phase 3 (`Hamnava.Core/Payments/ZarinpalPaymentGateway.cs`)
- **Test Status:** Not yet sandbox-tested (requires `.NET 10` runtime + Sandbox Merchant ID)

---

## 1. Verified Official Endpoints

### Production Environment
| Operation | HTTP Method | Endpoint |
| :--- | :--- | :--- |
| Payment Request | `POST` | `https://payment.zarinpal.com/pg/v4/payment/request.json` (also `https://api.zarinpal.com/pg/v4/payment/request.json`) |
| Payment Redirect | `GET` (Browser) | `https://payment.zarinpal.com/pg/StartPay/{authority}` (also `https://www.zarinpal.com/pg/StartPay/{authority}`) |
| Payment Verify | `POST` | `https://payment.zarinpal.com/pg/v4/payment/verify.json` (also `https://api.zarinpal.com/pg/v4/payment/verify.json`) |

### Sandbox Environment
| Operation | HTTP Method | Endpoint |
| :--- | :--- | :--- |
| Payment Request | `POST` | `https://sandbox.zarinpal.com/pg/v4/payment/request.json` |
| Payment Redirect | `GET` (Browser) | `https://sandbox.zarinpal.com/pg/StartPay/{authority}` |
| Payment Verify | `POST` | `https://sandbox.zarinpal.com/pg/v4/payment/verify.json` |

---

## 2. Authentication & Configuration
- Authentication uses a 36-character `merchant_id` supplied in the JSON request body.
- In Sandbox mode, any valid 36-character GUID string may be used per Zarinpal Sandbox rules.
- All credentials are injected via `PaymentGatewayOptions` from environment variables (`PaymentGateway__Zarinpal__MerchantId`), never committed to source control.
- Required headers:
  - `Content-Type: application/json`
  - `Accept: application/json`

---

## 3. Request & Response Contracts

### 3.1 Payment Request (`request.json`)
```json
{
  "merchant_id": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "amount": 3600000,
  "currency": "IRR",
  "callback_url": "https://hamnavamusic.ir/payments/zarinpal/callback",
  "description": "شهریه دوره آموزش ویولن - هم‌نوا",
  "metadata": {
    "mobile": "09121234567",
    "order_id": "PAYMENT_GUID"
  }
}
```
- **Currency Discipline:** Hamnava stores all internal amounts in **IRR** (`decimal`) and sends `currency: "IRR"` with integer Rial amounts to Zarinpal.
- **Success Response (`data.code == 100`):** Returns `data.authority`. Redirect user to `StartPay/{authority}`.

### 3.2 Callback Handling (Never Trust Alone)
Zarinpal redirects the user browser to `callback_url?Authority={authority}&Status={OK|NOK}`.
- If `Status != "OK"`, mark the attempt failed/canceled and show Persian feedback.
- If `Status == "OK"`, look up the `Payment` by unique `GatewayAuthority` and execute server-to-server verification (`verify.json`) using the **database-stored** `FinalAmountIrr` (never trusting any client query value).

### 3.3 Payment Verification (`verify.json`)
```json
{
  "merchant_id": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "amount": 3600000,
  "authority": "A00000000000000000000000000000000000"
}
```
- **`data.code == 100`:** First-time verified payment. Persist `ref_id`, `card_pan`, `VerifiedAtUtc`, and execute atomic business fulfillment (`Enrollment`, `Rental`, `Consultation`) inside a SQL Server transaction.
- **`data.code == 101`:** Already verified payment (duplicate callback / browser refresh). Treat idempotently: do NOT re-trigger enrollment activation, inventory deduction, referral reward, or wallet entries; show existing receipt.

---

## 4. Known Limitations & Pending Verification
- Production credentials must be provisioned on the deployment server only.
- Sandbox end-to-end tests will be executed once running on a `.NET 10` + SQL Server host.
