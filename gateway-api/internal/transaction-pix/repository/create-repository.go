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
	errCreate := r.db.QueryRow(queryInsert, transaction.Name, transaction.ExternalId, transaction.Value, qr_code).Scan(&transactionId)

	if errCreate != nil {
		fmt.Println("Failed in create transaction")
		fmt.Println(errCreate)
	}

	transactionCreated, errGet := r.GetById(transactionId)

	if errGet != nil {
		fmt.Println("Failed in create transaction")
		fmt.Println(errGet)
	}

	r.db.Close()

	fmt.Println(transactionCreated)

	return transactionCreated
}
