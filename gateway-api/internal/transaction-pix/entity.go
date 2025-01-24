package transaction_pix

type TransactionPix struct {
	Name       string  `json:"name"`
	ExternalId string  `json:"external_id"`
	Value      float64 `json:"value"`
}
