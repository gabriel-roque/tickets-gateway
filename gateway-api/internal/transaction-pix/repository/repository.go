package transaction_pix_repository

import (
	"github.com/gabriel-roque/gateway-api/pkg/database"
	"gorm.io/gorm"
)

type Repository struct {
	db        *gorm.DB
	initError error
}

func NewTransactionPixRepository() *Repository {
	db, err := database.CreateConnection()
	return &Repository{db: db, initError: err}
}
