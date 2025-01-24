package transaction_interfaces

type CreateTransactionPix struct {
	Name       string  `json:"name" binding:"required"`
	ExternalId string  `json:"external_id" binding:"required"`
	Value      float64 `json:"value" binding:"required"`
}
