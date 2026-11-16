package transaction_pix_repository

import (
	transaction_pix "github.com/gabriel-roque/gateway-api/internal/transaction-pix"
	transaction_interfaces "github.com/gabriel-roque/gateway-api/pkg/interfaces"
	"github.com/gabriel-roque/gateway-api/pkg/qrcode"
	"github.com/google/uuid"
	"gorm.io/gorm/clause"
)

func (r *Repository) Save(transactionDTO *transaction_interfaces.CreateTransactionPix) (transaction_pix.TransactionPix, error) {
	if r.initError != nil {
		return transaction_pix.TransactionPix{}, r.initError
	}
	qr_code := qrcode.GenerateQRCodePix(transactionDTO.Value, transactionDTO.Name)

	transaction := transaction_pix.TransactionPix{
		Id:         uuid.New().String(),
		Name:       transactionDTO.Name,
		Value:      transactionDTO.Value,
		ExternalId: transactionDTO.ExternalId,
		QrCode:     qr_code,
		Status:     false,
	}

	if err := r.db.Clauses(clause.OnConflict{
		Columns:   []clause.Column{{Name: "external_id"}},
		DoNothing: true,
	}).Create(&transaction).Error; err != nil {
		return transaction_pix.TransactionPix{}, err
	}

	var persisted transaction_pix.TransactionPix
	if err := r.db.Where("external_id = ?", transactionDTO.ExternalId).First(&persisted).Error; err != nil {
		return transaction_pix.TransactionPix{}, err
	}
	return persisted, nil
}
