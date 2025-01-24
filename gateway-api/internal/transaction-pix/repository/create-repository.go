package transaction_pix_create_repository

import (
	"database/sql"
	"fmt"

	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
	"github.com/gabriel-roque/tickets-gateway/pkg/database"
)

type repository struct {
	db *sql.DB
}

func NewCreateTransactionPixRepository() *repository {
	db := database.CreateConnection()
	return &repository{db}

}

func (r *repository) Save(transaction *transaction_pix.TransactionPix) {
	fmt.Printf("REPO")
}
