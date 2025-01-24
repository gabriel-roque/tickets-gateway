package transaction_pix_handlers

import (
	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
)

func (u *TransactionPixHandler) GetById(id string) transaction_pix.TransactionPix {
	return u.repository.GetById(id)
}
