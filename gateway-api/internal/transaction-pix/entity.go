package transaction_pix

type TransactionPix struct {
	Id         string  `json:"id" db:"id"`
	Name       string  `json:"name" db:"name"`
	ExternalId string  `json:"external_id" db:"external_id"`
	Value      float64 `json:"value" db:"value"`
	QrCode     string  `json:"qr_code" db:"qr_code"`
}
