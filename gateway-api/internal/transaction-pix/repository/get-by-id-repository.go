package transaction_pix_repository

import (
	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
)

func (r *Repository) GetById(id string) (transaction_pix.TransactionPix, error) {
	var transaction transaction_pix.TransactionPix

	r.db.First(&transaction, "id = ?", id)

	return transaction, r.db.Error
}
