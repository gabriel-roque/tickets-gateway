package transaction_pix_repository

import (
	"github.com/gabriel-roque/tickets-gateway/pkg/database"
	"github.com/jmoiron/sqlx"
)

type Repository struct {
	db *sqlx.DB
}

func NewTransactionPixRepository() *Repository {
	db := database.CreateConnection()
	return &Repository{db}
}
