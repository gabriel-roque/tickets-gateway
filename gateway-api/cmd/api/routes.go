package main

import (
	controllers "github.com/gabriel-roque/tickets-gateway/cmd/api/controllers/transaction"
	"github.com/gabriel-roque/tickets-gateway/pkg/middlewares"
	"github.com/gin-gonic/gin"
)

// TODO: Middleware X-API-KEY
func TransactionPixRoutes(router *gin.Engine) {
	transactionPixRoutes := router.Group("/transaction-pix")

	transactionPixRoutes.Use(middlewares.ApiKeyMiddleware())

	transactionPixRoutes.POST("/", controllers.CreateTransactionPix)
	transactionPixRoutes.GET("/:id", controllers.GetTransactionPix)
}
