package transaction_pix

type TransactionPix struct {
	Id         string  `json:"id"`
	Name       string  `json:"name"`
	ExternalId string  `json:"external_id"`
	Value      float64 `json:"value"`
	QrCode     string  `json:"qr_code"`
}
