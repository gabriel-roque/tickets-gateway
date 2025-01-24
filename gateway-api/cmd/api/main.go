package main

import (
	"github.com/gabriel-roque/tickets-gateway/pkg/database"
	"github.com/gin-gonic/gin"
)

func main() {
	router := gin.Default()

	router.GET("/healthy", func(c *gin.Context) {
		ping := database.DatabasePing()

		c.JSON(200, gin.H{
			"api":      true,
			"database": ping,
		})
	})

	TransactionPixRoutes(router)

	router.Run(":3000")
}
