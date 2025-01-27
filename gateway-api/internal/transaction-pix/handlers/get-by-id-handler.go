package transaction_pix_handlers

import (
	"errors"

	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
)

func (u *TransactionPixHandler) GetById(id string) (transaction_pix.TransactionPix, error) {
	transaction := u.repository.GetById(id)

	if transaction.Id != id {
		return transaction_pix.TransactionPix{}, errors.New("Not found")
	}

	return transaction, nil
}
