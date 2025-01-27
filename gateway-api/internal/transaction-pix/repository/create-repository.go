package transaction_pix_repository

import (
	"fmt"

	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
	transaction_interfaces "github.com/gabriel-roque/tickets-gateway/pkg/interfaces"
	"github.com/gabriel-roque/tickets-gateway/pkg/qrcode"
)

func (r *Repository) Save(transaction *transaction_interfaces.CreateTransactionPix) transaction_pix.TransactionPix {
	qr_code := qrcode.GenerateQRCodePix(transaction.Value, transaction.Name)

	var transactionId string

	queryInsert := `
		INSERT INTO transaction_pix (name, external_id, value, qr_code)
		VALUES ($1, $2, $3, $4)
		RETURNING id
	`
	err := r.db.QueryRow(queryInsert, transaction.Name, transaction.ExternalId, transaction.Value, qr_code).Scan(&transactionId)

	transactionCreated := r.GetById(transactionId)
	fmt.Println(transactionCreated)

	if err != nil {
		fmt.Println("Failed in create transaction")
	}

	return transactionCreated
}
