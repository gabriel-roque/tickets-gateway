package controllers

import (
	"net/http"

	transaction_pix "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix"
	transaction_pix_handlers "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix/handlers"
	"github.com/gin-gonic/gin"
)

func CreateTransactionPix(ctx *gin.Context) {
	var body transaction_pix.TransactionPix

	if err := ctx.ShouldBindJSON(&body); err != nil {
		ctx.AbortWithStatusJSON(http.StatusBadRequest,
			gin.H{
				"success": false,
				"error":   err.Error(),
			})
		return
	}

	transaction_pix_handlers.NewCreateTransactionPixHandler().Execute(body)

	ctx.JSON(http.StatusCreated, gin.H{"success": true})
}
