package transaction_pix_repository

import (
	"database/sql"
	"fmt"

	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
	"github.com/gabriel-roque/tickets-gateway/pkg/database"
	transaction_interfaces "github.com/gabriel-roque/tickets-gateway/pkg/interfaces"
)

type repository struct {
	db *sql.DB
}

func NewCreateTransactionPixRepository() *repository {
	db := database.CreateConnection()
	return &repository{db}

}

func (r *repository) Save(transaction *transaction_interfaces.CreateTransactionPix) {
	// TODO: create function generate qr_code
	qr_code := "fake_qr_code"

	var transactionId string
	var transactionCreated transaction_pix.TransactionPix

	queryInsert := `
		INSERT INTO transaction_pix (name, external_id, value, qr_code)
		VALUES ($1, $2, $3, $4)
		RETURNING id
	`

	err := r.db.QueryRow(queryInsert, transaction.Name, transaction.ExternalId, transaction.Value, qr_code).Scan(&transactionId)

	queryGet := `
		SELECT id, name
		FROM transaction_pix
		WHERE id = $1
	`
	fmt.Println(transactionId)

	r.db.QueryRow(queryGet, transactionId).Scan(&transactionCreated.Id, &transactionCreated.Name)

	fmt.Println(transactionCreated)

	if err != nil {
		fmt.Println("Failed in create transaction")
	}
}
