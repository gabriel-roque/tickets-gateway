package transaction_pix_repository

import (
	"database/sql"

	"github.com/gabriel-roque/tickets-gateway/pkg/database"
)

type Repository struct {
	db *sql.DB
}

func NewTransactionPixRepository() *Repository {
	db := database.CreateConnection()
	return &Repository{db}
}
