package transaction_pix_repository

import (
	transaction_pix "github.com/gabriel-roque/gateway-api/internal/transaction-pix"
)

func (r *Repository) GetById(id string) (transaction_pix.TransactionPix, error) {
	if r.initError != nil {
		return transaction_pix.TransactionPix{}, r.initError
	}
	var transaction transaction_pix.TransactionPix

	result := r.db.First(&transaction, "id = ?", id)
	return transaction, result.Error
}
