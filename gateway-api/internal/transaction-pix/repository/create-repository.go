package transaction_pix_repository

import (
	"fmt"

	transaction_interfaces "github.com/gabriel-roque/tickets-gateway/pkg/interfaces"
)

func (r *Repository) Save(transaction *transaction_interfaces.CreateTransactionPix) {
	// TODO: create function generate qr_code
	qr_code := "fake_qr_code"

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
}
