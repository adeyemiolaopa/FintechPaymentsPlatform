# Settlement Files

`POST /api/v1/admin/settlements/{settlementDate}` returns settlement records for successful transfers completed on that UTC date.

Supported anomaly modes:

- `normal`
- `missing`
- `duplicate`
- `amount_mismatch`
- `unknown_reference`

These modes let payment reconciliation code prove handling for common provider settlement defects without calling a real bank or payment processor.
