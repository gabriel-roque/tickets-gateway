package transaction_pix_repository

import (
	"github.com/gabriel-roque/tickets-gateway/pkg/database"
	"gorm.io/gorm"
)

type Repository struct {
	db *gorm.DB
}

func NewTransactionPixRepository() *Repository {
	db, _ := database.CreateConnection()
	return &Repository{db}
}
