package transaction_pix

type TransactionPix struct {
	Id         string  `json:"id" gorm:"column:id;primaryKey"`
	Name       string  `json:"name" gorm:"column:name"`
	ExternalId string  `json:"external_id" gorm:"column:external_id"`
	Value      float64 `json:"value" gorm:"column:value"`
	QrCode     string  `json:"qr_code" gorm:"column:qr_code"`
	Status     bool    `json:"status" gorm:"column:status"`
}

func (TransactionPix) TableName() string {
	return "transaction_pix"
}
