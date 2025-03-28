package transaction_pix_repository

import (
	"fmt"

	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
	transaction_interfaces "github.com/gabriel-roque/tickets-gateway/pkg/interfaces"
	"github.com/gabriel-roque/tickets-gateway/pkg/qrcode"
	"github.com/google/uuid"
)

func (r *Repository) Save(transactionDTO *transaction_interfaces.CreateTransactionPix) transaction_pix.TransactionPix {
	sql, _ := r.db.DB()

	qr_code := qrcode.GenerateQRCodePix(transactionDTO.Value, transactionDTO.Name)

	transaction := transaction_pix.TransactionPix{
		Id:         uuid.New().String(),
		Name:       transactionDTO.Name,
		Value:      transactionDTO.Value,
		ExternalId: transactionDTO.ExternalId,
		QrCode:     qr_code,
		Status:     false,
	}

	errCreate := r.db.Create(&transaction)

	transactionId := transaction.Id

	if errCreate != nil {
		fmt.Println("Failed in create transaction")
	}

	transactionCreated, errGet := r.GetById(transactionId)

	if errGet != nil {
		fmt.Println("Failed in create transaction")
		// fmt.Println(errGet)
	}

	sql.Close()

	// fmt.Println(transactionCreated)

	return transactionCreated
}
