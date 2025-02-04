package transaction_pix_repository

import (
	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
)

func (r *Repository) GetById(id string) (transaction_pix.TransactionPix, error) {
	var transaction transaction_pix.TransactionPix

	queryGet := `
        SELECT id, name, external_id, value, qr_code
        FROM transaction_pix
        WHERE id = $1
    `

	err := r.db.Get(&transaction, queryGet, id)
	if err != nil {
		return transaction_pix.TransactionPix{}, err
	}

	return transaction, nil
}
